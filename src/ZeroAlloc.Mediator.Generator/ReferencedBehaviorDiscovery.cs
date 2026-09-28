#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Pipeline.Generators;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// Finds pipeline behaviors that live in referenced assemblies, such as the bridge packages'
    /// <c>CacheBehavior</c> or <c>AuthorizationBehavior</c>.
    /// </summary>
    /// <remarks>
    /// <para><c>ForAttributeWithMetadataName</c> only sees the syntax trees of the compilation
    /// being generated, so a behavior shipped in a package is invisible to it. Without this, a
    /// bridge package's behavior never ran through the generated <c>Send</c>, whatever the app
    /// registered.</para>
    /// <para>A referenced type joins the pipeline when it is a public, non-generic class that
    /// implements <c>ZeroAlloc.Mediator.IPipelineBehavior</c>, carries a
    /// <c>PipelineBehaviorAttribute</c>, and has a public static two-type-parameter
    /// <c>Handle</c>. Requiring Mediator's own marker interface keeps out behaviors other
    /// ZeroAlloc libraries build on <c>ZeroAlloc.Pipeline</c> for their own pipelines. Only
    /// assemblies that reference <c>ZeroAlloc.Mediator</c> are walked.</para>
    /// </remarks>
    internal static class ReferencedBehaviorDiscovery
    {
        private const string MediatorAssemblyName = "ZeroAlloc.Mediator";
        private const string MediatorBehaviorInterface = "ZeroAlloc.Mediator.IPipelineBehavior";
        private const string PipelineBehaviorAttribute = "ZeroAlloc.Pipeline.PipelineBehaviorAttribute";
        private const int SendHandleTypeParameterCount = 2;

        public static BehaviorList Discover(Compilation compilation, CancellationToken ct)
        {
            var marker = compilation.GetTypeByMetadataName(MediatorBehaviorInterface);
            var attributeBase = compilation.GetTypeByMetadataName(PipelineBehaviorAttribute);
            if (marker == null || attributeBase == null)
                return BehaviorList.Empty;

            var found = ImmutableArray.CreateBuilder<PipelineBehaviorInfo>();
            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferencesMediator(assembly))
                    continue;

                CollectFromNamespace(assembly.GlobalNamespace, marker, attributeBase, found, ct);
            }

            // Symbol enumeration order is not part of any contract; sort so the generated source
            // is stable. The pipeline is ordered by Order afterwards, with this as the tiebreak.
            return new BehaviorList(found
                .OrderBy(static b => b.BehaviorTypeName, StringComparer.Ordinal)
                .ToImmutableArray());
        }

        private static bool ReferencesMediator(IAssemblySymbol assembly)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var reference in module.ReferencedAssemblies)
                {
                    if (string.Equals(reference.Name, MediatorAssemblyName, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private static void CollectFromNamespace(
            INamespaceSymbol ns,
            INamedTypeSymbol marker,
            INamedTypeSymbol attributeBase,
            ImmutableArray<PipelineBehaviorInfo>.Builder found,
            CancellationToken ct)
        {
            foreach (var member in ns.GetMembers())
            {
                ct.ThrowIfCancellationRequested();
                if (member is INamespaceSymbol childNamespace)
                    CollectFromNamespace(childNamespace, marker, attributeBase, found, ct);
                else if (member is INamedTypeSymbol type)
                    CollectFromType(type, marker, attributeBase, found);
            }
        }

        private static void CollectFromType(
            INamedTypeSymbol type,
            INamedTypeSymbol marker,
            INamedTypeSymbol attributeBase,
            ImmutableArray<PipelineBehaviorInfo>.Builder found)
        {
            // Accessibility is checked per level, so a nested type is only reached through a
            // public container.
            if (type.DeclaredAccessibility != Accessibility.Public)
                return;

            var info = TryCreateInfo(type, marker, attributeBase);
            if (info != null)
                found.Add(info);

            foreach (var nested in type.GetTypeMembers())
                CollectFromType(nested, marker, attributeBase, found);
        }

        private static PipelineBehaviorInfo? TryCreateInfo(
            INamedTypeSymbol type,
            INamedTypeSymbol marker,
            INamedTypeSymbol attributeBase)
        {
            if (type.TypeKind != TypeKind.Class || type.IsGenericType)
                return null;
            if (!type.AllInterfaces.Contains(marker, SymbolEqualityComparer.Default))
                return null;

            AttributeData? attribute = null;
            foreach (var candidate in type.GetAttributes())
            {
                if (InheritsFrom(candidate.AttributeClass, attributeBase))
                {
                    attribute = candidate;
                    break;
                }
            }
            if (attribute == null)
                return null;

            if (GetHandleTypeParameterCount(type) != SendHandleTypeParameterCount)
                return null;

            return new PipelineBehaviorInfo(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ReadOrder(attribute),
                ReadAppliesTo(attribute),
                SendHandleTypeParameterCount);
        }

        private static bool InheritsFrom(INamedTypeSymbol? type, INamedTypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                    return true;
            }
            return false;
        }

        private static int ReadOrder(AttributeData attribute)
        {
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Order" && named.Value.Value is int namedOrder)
                    return namedOrder;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is int constructorOrder)
                return constructorOrder;

            return 0;
        }

        private static string? ReadAppliesTo(AttributeData attribute)
        {
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "AppliesTo" && named.Value.Value is ITypeSymbol target)
                    return target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            return null;
        }

        private static int GetHandleTypeParameterCount(INamedTypeSymbol type)
        {
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var member in current.GetMembers("Handle"))
                {
                    if (member is IMethodSymbol method
                        && method.IsStatic
                        && method.DeclaredAccessibility == Accessibility.Public
                        && method.TypeParameters.Length > 0)
                        return method.TypeParameters.Length;
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// The referenced behaviors, compared by content so the incremental pipeline can cache on it.
    /// <see cref="ImmutableArray{T}"/> compares by reference, which would rerun every step
    /// downstream on each edit.
    /// </summary>
    internal sealed class BehaviorList : IEquatable<BehaviorList>
    {
        public static readonly BehaviorList Empty = new BehaviorList(ImmutableArray<PipelineBehaviorInfo>.Empty);

        public BehaviorList(ImmutableArray<PipelineBehaviorInfo> items) => Items = items;

        public ImmutableArray<PipelineBehaviorInfo> Items { get; }

        public bool Equals(BehaviorList? other)
            => other != null && Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) => Equals(obj as BehaviorList);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var item in Items)
                    hash = (hash * 31) + item.GetHashCode();
                return hash;
            }
        }
    }
}
