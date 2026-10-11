#nullable enable
using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// A [CacheResponse] request type whose cache key cannot tell its instances apart, for ZAM010.
    /// </summary>
    /// <remarks>
    /// CacheBehavior in ZeroAlloc.Mediator.Cache keys an entry by
    /// <c>"&lt;request type full name&gt;:&lt;request&gt;"</c>, an interpolation that formats the
    /// request through <see cref="IFormattable"/> when the type implements it, and through
    /// <c>ToString()</c> otherwise. A class or struct that does neither, and is not a record,
    /// formats as its type name, so every instance shares one entry and is answered with the first
    /// response. A type without instance state is left alone: all its instances are the same
    /// request, and one entry is right.
    /// </remarks>
    internal sealed class CachedRequestKeyInfo : IEquatable<CachedRequestKeyInfo>
    {
        /// <summary>The attribute that marks a cached request, which ForAttributeWithMetadataName matches.</summary>
        public const string AttributeMetadataName = "ZeroAlloc.Mediator.Cache.CacheResponseAttribute";

        public CachedRequestKeyInfo(string requestTypeName, LocationInfo? location)
        {
            RequestTypeName = requestTypeName;
            Location = location;
        }

        public string RequestTypeName { get; }

        /// <summary>The type identifier. Part of equality, so a cached model never keeps a stale location.</summary>
        public LocationInfo? Location { get; }

        /// <summary>The model to report, or null when the type's key identifies each instance.</summary>
        public static CachedRequestKeyInfo? From(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            if (context.TargetSymbol is not INamedTypeSymbol type
                || context.TargetNode is not TypeDeclarationSyntax declaration)
                return null;

            if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
                return null;

            if (!IsRequest(type) || HasValueText(type) || !HasInstanceState(type, ct))
                return null;

            return new CachedRequestKeyInfo(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                LocationInfo.From(declaration.Identifier.GetLocation()));
        }

        private static bool IsRequest(INamedTypeSymbol type) =>
            type.AllInterfaces.Any(static i =>
                i.TypeArguments.Length == 1
                && i.OriginalDefinition.ToDisplayString() == "ZeroAlloc.Mediator.IRequest<TResponse>");

        // Whether the key interpolation formats an instance as something other than its type name.
        // A record's compiler-written ToString prints its members; IFormattable, and
        // ISpanFormattable which extends it, take precedence over ToString in the interpolation.
        private static bool HasValueText(INamedTypeSymbol type)
        {
            if (type.IsRecord)
                return true;

            if (type.AllInterfaces.Any(static i => i.ToDisplayString() == "System.IFormattable"))
                return true;

            for (var current = type; current != null && !IsRootType(current); current = current.BaseType)
            {
                foreach (var member in current.GetMembers("ToString"))
                {
                    if (member is IMethodSymbol { IsOverride: true, IsStatic: false, Parameters.Length: 0 })
                        return true;
                }
            }

            return false;
        }

        // Whether instances of the type can differ: an instance field, an auto-implemented
        // property, or a primary constructor parameter, on the type or a base type. A property of
        // a referenced base type cannot be inspected for a backing field, so it counts as state.
        private static bool HasInstanceState(INamedTypeSymbol type, CancellationToken ct)
        {
            for (var current = type; current != null && !IsRootType(current); current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member.IsStatic)
                        continue;

                    switch (member)
                    {
                        case IFieldSymbol { IsConst: false }:
                            return true;
                        case IPropertySymbol { IsIndexer: false } property when IsStoredProperty(property, ct):
                            return true;
                    }
                }

                if (HasPrimaryConstructorParameters(current, ct))
                    return true;
            }

            return false;
        }

        private static bool IsStoredProperty(IPropertySymbol property, CancellationToken ct)
        {
            if (property.DeclaringSyntaxReferences.Length == 0)
                return true;

            foreach (var reference in property.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(ct) is not PropertyDeclarationSyntax syntax)
                    return true;

                if (syntax.ExpressionBody != null || syntax.AccessorList == null)
                    continue;

                foreach (var accessor in syntax.AccessorList.Accessors)
                {
                    if (accessor.Body == null && accessor.ExpressionBody == null)
                        return true;
                    if (accessor.DescendantNodes().OfType<FieldExpressionSyntax>().Any())
                        return true;
                }
            }

            return false;
        }

        private static bool HasPrimaryConstructorParameters(INamedTypeSymbol type, CancellationToken ct)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(ct) is TypeDeclarationSyntax { ParameterList.Parameters.Count: > 0 })
                    return true;
            }

            return false;
        }

        private static bool IsRootType(INamedTypeSymbol type) =>
            type.SpecialType == SpecialType.System_Object || type.SpecialType == SpecialType.System_ValueType;

        public bool Equals(CachedRequestKeyInfo? other) =>
            other is not null
            && RequestTypeName == other.RequestTypeName
            && Equals(Location, other.Location);

        public override bool Equals(object? obj) => Equals(obj as CachedRequestKeyInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                return (RequestTypeName.GetHashCode() * 31) + (Location?.GetHashCode() ?? 0);
            }
        }
    }
}
