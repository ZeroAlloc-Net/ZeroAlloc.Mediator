#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// The members of a [CacheResponse] record request that print as their type name in the
    /// record's compiler-written ToString, for ZAM011.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CacheBehavior keys an entry by the request's text. For a record that is its compiler-written
    /// ToString, which prints each member the compiler-written PrintMembers selects through that
    /// member's own <c>ToString()</c>: public instance fields and readable public instance
    /// properties that are not overrides or indexers, after the members of a base record. A member
    /// whose type keeps <see cref="object.ToString"/>, such as an array or a <c>List&lt;T&gt;</c>,
    /// prints as its type name, so requests that differ only in it share one entry.
    /// </para>
    /// <para>
    /// PrintMembers appends a member with <c>ToString()</c>, not through <see cref="IFormattable"/>,
    /// so implementing <see cref="IFormattable"/> does not give a member a string form; only a
    /// <c>ToString()</c> override does. Every <see cref="IFormattable"/> type in the BCL overrides it.
    /// </para>
    /// <para>
    /// A member typed as a type parameter, an interface that is not enumerable, <c>object</c> or
    /// <c>dynamic</c> is not reported: its text depends on the runtime type. No constraint makes a
    /// type parameter certain to fail, since a class constraint admits a derived class that
    /// overrides ToString, and an interface constraint admits a type that does.
    /// </para>
    /// </remarks>
    internal sealed class CachedRecordMemberKeyInfo : IEquatable<CachedRecordMemberKeyInfo>
    {
        public CachedRecordMemberKeyInfo(string requestTypeName, ImmutableArray<OpaqueMemberInfo> members)
        {
            RequestTypeName = requestTypeName;
            Members = members;
        }

        public string RequestTypeName { get; }

        public ImmutableArray<OpaqueMemberInfo> Members { get; }

        /// <summary>The model to report, or null when every printed member has a string form.</summary>
        public static CachedRecordMemberKeyInfo? From(GeneratorAttributeSyntaxContext context, CancellationToken ct)
        {
            if (context.TargetSymbol is not INamedTypeSymbol type
                || context.TargetNode is not TypeDeclarationSyntax declaration)
                return null;

            if (!type.IsRecord || !CachedRequestKeyInfo.IsRequest(type))
                return null;

            // The key interpolation formats an IFormattable request through that interface, and a
            // record that writes ToString or PrintMembers itself decides what its text shows.
            if (ImplementsInterface(type, "System.IFormattable") || DeclaresOwnText(type))
                return null;

            var fallback = LocationInfo.From(declaration.Identifier.GetLocation());
            var found = new List<OpaqueMemberInfo>();
            var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };

            foreach (var member in PrintedMembers(type))
            {
                ct.ThrowIfCancellationRequested();
                var leaves = new List<(string Path, ITypeSymbol Type)>();
                CollectOpaque(MemberType(member), member.Name, leaves, visiting);

                foreach (var (path, leafType) in leaves)
                {
                    found.Add(new OpaqueMemberInfo(
                        path,
                        leafType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        MemberLocation(member, context.SemanticModel.Compilation, ct) ?? fallback));
                }
            }

            return found.Count == 0
                ? null
                : new CachedRecordMemberKeyInfo(
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    found.ToImmutableArray());
        }

        // Whether the record's ToString is not the compiler-written one that prints its members:
        // it declares ToString or PrintMembers, or a base record seals its ToString, which a
        // derived record then inherits instead of writing its own.
        private static bool DeclaresOwnText(INamedTypeSymbol record)
        {
            if (DeclaresMethod(record, "ToString") || DeclaresMethod(record, "PrintMembers"))
                return true;

            for (var current = record.BaseType; current != null && current.IsRecord; current = current.BaseType)
            {
                foreach (var member in current.GetMembers("ToString"))
                {
                    if (member is IMethodSymbol { IsSealed: true, IsStatic: false, Parameters.Length: 0 })
                        return true;
                }
            }

            return false;
        }

        private static bool DeclaresMethod(INamedTypeSymbol type, string name) =>
            type.GetMembers(name).Any(static m =>
                m is IMethodSymbol { IsStatic: false, IsImplicitlyDeclared: false } method
                && (method.Name != "ToString" || method.Parameters.Length == 0));

        // The members the compiler-written PrintMembers prints, base record first. A base record
        // that declares PrintMembers prints its own members, and its bases', its own way.
        private static IEnumerable<ISymbol> PrintedMembers(INamedTypeSymbol record)
        {
            var levels = new List<INamedTypeSymbol>();
            for (var current = record; current != null && current.IsRecord; current = current.BaseType)
            {
                if (!SymbolEqualityComparer.Default.Equals(current, record) && DeclaresMethod(current, "PrintMembers"))
                    break;
                levels.Add(current);
            }

            for (var i = levels.Count - 1; i >= 0; i--)
            {
                foreach (var member in levels[i].GetMembers())
                {
                    if (IsPrinted(member))
                        yield return member;
                }
            }
        }

        private static bool IsPrinted(ISymbol member)
        {
            if (member.DeclaredAccessibility != Accessibility.Public || member.IsStatic)
                return false;

            return member switch
            {
                IFieldSymbol => true,
                IPropertySymbol property => !property.IsIndexer && !property.IsOverride && property.GetMethod != null,
                _ => false,
            };
        }

        private static ITypeSymbol MemberType(ISymbol member) =>
            member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;

        // Adds each part of a value of this type that prints as its type name, with the path to it.
        private static void CollectOpaque(
            ITypeSymbol type, string path, List<(string, ITypeSymbol)> found, HashSet<ITypeSymbol> visiting)
        {
            switch (type)
            {
                case IArrayTypeSymbol:
                    found.Add((path, type));
                    return;
                case INamedTypeSymbol named:
                    CollectOpaqueNamed(named, path, found, visiting);
                    return;
                default:
                    // Type parameters, dynamic, pointers and error types: nothing known to report.
                    return;
            }
        }

        private static void CollectOpaqueNamed(
            INamedTypeSymbol type, string path, List<(string, ITypeSymbol)> found, HashSet<ITypeSymbol> visiting)
        {
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                CollectOpaque(type.TypeArguments[0], path, found, visiting);
                return;
            }

            if (type.IsTupleType)
            {
                // ValueTuple prints each element by its own ToString.
                foreach (var element in type.TupleElements)
                    CollectOpaque(element.Type, path + "." + element.Name, found, visiting);
                return;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Object:
                case SpecialType.System_ValueType:
                case SpecialType.System_Enum:
                case SpecialType.System_String:
                    return;
            }

            switch (type.TypeKind)
            {
                case TypeKind.Enum:
                    return;
                case TypeKind.Delegate:
                    found.Add((path, type));
                    return;
                case TypeKind.Interface:
                    // The runtime type decides, unless every implementation is a sequence.
                    if (IsSequence(type))
                        found.Add((path, type));
                    return;
                case TypeKind.Class:
                case TypeKind.Struct:
                    break;
                default:
                    return;
            }

            if (type.IsRecord)
            {
                if (DeclaresOwnText(type) || !visiting.Add(type))
                    return;

                foreach (var member in PrintedMembers(type))
                    CollectOpaque(MemberType(member), path + "." + member.Name, found, visiting);

                visiting.Remove(type);
                return;
            }

            if (IsGeneric(type, "System.Collections.Generic", "KeyValuePair", 2))
            {
                // Prints "[key, value]", each by its own ToString.
                CollectOpaque(type.TypeArguments[0], path + ".Key", found, visiting);
                CollectOpaque(type.TypeArguments[1], path + ".Value", found, visiting);
                return;
            }

            if (type.ContainingNamespace?.ToDisplayString() == "System" && type.Name == "Tuple" && type.IsGenericType)
            {
                for (var i = 0; i < type.TypeArguments.Length; i++)
                    CollectOpaque(type.TypeArguments[i], path + ".Item" + (i + 1), found, visiting);
                return;
            }

            // Memory<T> and ReadOnlyMemory<T> override ToString, but print only their length
            // unless T is char.
            if ((IsGeneric(type, "System", "Memory", 1) || IsGeneric(type, "System", "ReadOnlyMemory", 1))
                && type.TypeArguments[0].SpecialType != SpecialType.System_Char)
            {
                found.Add((path, type));
                return;
            }

            if (OverridesToString(type))
                return;

            found.Add((path, type));
        }

        private static bool OverridesToString(INamedTypeSymbol type)
        {
            for (var current = type; current != null && !CachedRequestKeyInfo.IsRootType(current); current = current.BaseType)
            {
                foreach (var member in current.GetMembers("ToString"))
                {
                    if (member is IMethodSymbol { IsOverride: true, IsStatic: false, Parameters.Length: 0 })
                        return true;
                }
            }

            return false;
        }

        private static bool IsSequence(INamedTypeSymbol type) =>
            type.SpecialType == SpecialType.System_Collections_IEnumerable
            || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
            || type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable);

        private static bool IsGeneric(INamedTypeSymbol type, string ns, string name, int arity) =>
            type.Name == name
            && type.Arity == arity
            && type.ContainingNamespace?.ToDisplayString() == ns;

        private static bool ImplementsInterface(INamedTypeSymbol type, string fullName) =>
            type.AllInterfaces.Any(i => i.ToDisplayString() == fullName);

        // The member's name in its declaration in this compilation: a positional parameter, a
        // property or a field. Null for a member of a base record from another assembly. In the
        // IDE a project reference is a compilation whose symbols keep their syntax, but a
        // diagnostic in a tree of that other compilation fails the generator.
        private static LocationInfo? MemberLocation(ISymbol member, Compilation compilation, CancellationToken ct)
        {
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                if (!compilation.ContainsSyntaxTree(reference.SyntaxTree))
                    continue;

                var identifier = reference.GetSyntax(ct) switch
                {
                    ParameterSyntax parameter => parameter.Identifier,
                    PropertyDeclarationSyntax property => property.Identifier,
                    VariableDeclaratorSyntax variable => variable.Identifier,
                    _ => default,
                };

                if (identifier != default)
                    return LocationInfo.From(identifier.GetLocation());
            }

            return null;
        }

        public bool Equals(CachedRecordMemberKeyInfo? other) =>
            other is not null
            && RequestTypeName == other.RequestTypeName
            && Members.SequenceEqual(other.Members);

        public override bool Equals(object? obj) => Equals(obj as CachedRecordMemberKeyInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = RequestTypeName.GetHashCode();
                foreach (var member in Members)
                    hash = (hash * 31) + member.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>A printed member, or a part of one, whose text is its type name.</summary>
    internal sealed class OpaqueMemberInfo : IEquatable<OpaqueMemberInfo>
    {
        public OpaqueMemberInfo(string path, string typeName, LocationInfo? location)
        {
            Path = path;
            TypeName = typeName;
            Location = location;
        }

        /// <summary>The member name, followed by the names inside it for a nested record or tuple.</summary>
        public string Path { get; }

        public string TypeName { get; }

        /// <summary>The top-level member's name. Part of equality, so a cached model never keeps a stale location.</summary>
        public LocationInfo? Location { get; }

        public bool Equals(OpaqueMemberInfo? other) =>
            other is not null
            && Path == other.Path
            && TypeName == other.TypeName
            && Equals(Location, other.Location);

        public override bool Equals(object? obj) => Equals(obj as OpaqueMemberInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                return (((Path.GetHashCode() * 31) + TypeName.GetHashCode()) * 31) + (Location?.GetHashCode() ?? 0);
            }
        }
    }
}
