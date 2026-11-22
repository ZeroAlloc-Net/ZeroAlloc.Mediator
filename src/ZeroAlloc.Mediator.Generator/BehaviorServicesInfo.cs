#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// What the generator needs to know about a behavior beyond its <c>Handle</c>: whether it has
    /// a <c>CheckStream</c> method.
    /// </summary>
    /// <remarks>
    /// The request's services reach a behavior as the <c>state</c> argument of <c>Handle</c>, so
    /// nothing about them is read from the behavior's source.
    /// </remarks>
    internal sealed class BehaviorServicesInfo : IEquatable<BehaviorServicesInfo>
    {
        private const string StreamCheckMethodName = "CheckStream";

        public BehaviorServicesInfo(string behaviorTypeName, bool hasStreamCheck = false)
        {
            BehaviorTypeName = behaviorTypeName;
            HasStreamCheck = hasStreamCheck;
        }

        /// <summary>Fully qualified type name, matching <c>PipelineBehaviorInfo.BehaviorTypeName</c>.</summary>
        public string BehaviorTypeName { get; }

        /// <summary>
        /// Whether the behavior has the <c>CheckStream&lt;TRequest&gt;(request, IServiceProvider?
        /// services, ct)</c> method, which the generated <c>CreateStream</c> runs before the
        /// stream handler. Streams are not wrapped by the pipeline, so this is how a behavior
        /// that decides before the first item, such as authorization, covers them.
        /// </summary>
        public bool HasStreamCheck { get; }

        public static BehaviorServicesInfo From(INamedTypeSymbol type) =>
            new BehaviorServicesInfo(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                HasStreamCheckMethod(type));

        // CheckStream<TRequest>(TRequest, IServiceProvider?, CancellationToken) -> ValueTask
        private static bool HasStreamCheckMethod(INamedTypeSymbol type)
        {
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(StreamCheckMethodName))
                {
                    if (member is IMethodSymbol method
                        && method.IsStatic
                        && method.DeclaredAccessibility == Accessibility.Public
                        && IsStreamCheckShape(method))
                        return true;
                }
            }
            return false;
        }

        private static bool IsStreamCheckShape(IMethodSymbol method)
        {
            if (method.TypeParameters.Length != 1 || method.Parameters.Length != 3)
                return false;

            var parameters = method.Parameters;
            return SymbolEqualityComparer.Default.Equals(parameters[0].Type, method.TypeParameters[0])
                && IsType(parameters[1].Type.WithNullableAnnotation(NullableAnnotation.None), "System.IServiceProvider")
                && IsType(parameters[2].Type, "System.Threading.CancellationToken")
                && IsType(method.ReturnType, "System.Threading.Tasks.ValueTask");
        }

        private static bool IsType(ITypeSymbol type, string name) =>
            string.Equals(type.ToDisplayString(), name, StringComparison.Ordinal);

        public bool Equals(BehaviorServicesInfo? other) =>
            other is not null
            && BehaviorTypeName == other.BehaviorTypeName
            && HasStreamCheck == other.HasStreamCheck;

        public override bool Equals(object? obj) => Equals(obj as BehaviorServicesInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = BehaviorTypeName.GetHashCode();
                hash = (hash * 31) + (HasStreamCheck ? 1 : 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// The <see cref="BehaviorServicesInfo"/> of every behavior, compared by content so the
    /// incremental pipeline can cache on it.
    /// </summary>
    internal sealed class BehaviorServicesMap : IEquatable<BehaviorServicesMap>
    {
        private readonly ImmutableArray<BehaviorServicesInfo> _items;
        private readonly Dictionary<string, BehaviorServicesInfo> _byName;

        public BehaviorServicesMap(ImmutableArray<BehaviorServicesInfo> items)
        {
            _items = items.OrderBy(static i => i.BehaviorTypeName, StringComparer.Ordinal).ToImmutableArray();
            _byName = new Dictionary<string, BehaviorServicesInfo>(StringComparer.Ordinal);
            foreach (var item in _items)
            {
                // A type found twice, through both attribute providers, is the same type.
                _byName[item.BehaviorTypeName] = item;
            }
        }

        /// <summary>
        /// The behavior's info. A behavior this map does not know has no <c>CheckStream</c>.
        /// </summary>
        public BehaviorServicesInfo Get(string behaviorTypeName) =>
            _byName.TryGetValue(behaviorTypeName, out var info)
                ? info
                : new BehaviorServicesInfo(behaviorTypeName);

        public bool Equals(BehaviorServicesMap? other) =>
            other is not null && _items.SequenceEqual(other._items);

        public override bool Equals(object? obj) => Equals(obj as BehaviorServicesMap);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var item in _items)
                    hash = (hash * 31) + item.GetHashCode();
                return hash;
            }
        }
    }
}
