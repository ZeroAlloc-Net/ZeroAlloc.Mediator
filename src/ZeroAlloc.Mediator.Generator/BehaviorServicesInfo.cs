#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// How a pipeline behavior reaches the services of the request it handles, which decides
    /// whether the generated <c>IMediator.Send</c> must set <c>PipelineBehaviorServices.Current</c>.
    /// </summary>
    /// <remarks>
    /// Setting <c>Current</c> is an <c>AsyncLocal</c> write and allocates on every <c>Send</c>, so
    /// it is emitted only when a behavior in the request's pipeline may read it: one that neither
    /// takes the provider as a <c>Handle</c> argument nor declares
    /// <c>UsesPipelineBehaviorServices = false</c>. Both are explicit in the behavior's source;
    /// nothing is inferred from what its body does.
    /// </remarks>
    internal sealed class BehaviorServicesInfo : IEquatable<BehaviorServicesInfo>
    {
        private const string MediatorAttribute = "ZeroAlloc.Mediator.PipelineBehaviorAttribute";
        private const string UsesServicesProperty = "UsesPipelineBehaviorServices";

        public BehaviorServicesInfo(string behaviorTypeName, bool takesServices, bool usesCurrent)
        {
            BehaviorTypeName = behaviorTypeName;
            TakesServices = takesServices;
            UsesCurrent = usesCurrent;
        }

        /// <summary>Fully qualified type name, matching <c>PipelineBehaviorInfo.BehaviorTypeName</c>.</summary>
        public string BehaviorTypeName { get; }

        /// <summary>
        /// Whether the behavior has the <c>Handle(request, IServiceProvider services, ct, next)</c>
        /// overload, which <c>IMediator.Send</c> calls with its provider.
        /// </summary>
        public bool TakesServices { get; }

        /// <summary>
        /// Whether the behavior's attribute leaves <c>UsesPipelineBehaviorServices</c> at its
        /// default, <see langword="true"/>.
        /// </summary>
        public bool UsesCurrent { get; }

        /// <summary>Whether <c>IMediator.Send</c> must set <c>PipelineBehaviorServices.Current</c> for it.</summary>
        public bool NeedsCurrent => UsesCurrent && !TakesServices;

        public static BehaviorServicesInfo From(INamedTypeSymbol type, AttributeData? attribute) =>
            new BehaviorServicesInfo(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                HasServicesHandle(type),
                ReadUsesServices(attribute));

        private static bool ReadUsesServices(AttributeData? attribute)
        {
            if (attribute?.AttributeClass?.ToDisplayString() != MediatorAttribute)
                return true;

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == UsesServicesProperty && named.Value.Value is bool value)
                    return value;
            }
            return true;
        }

        // Handle<TRequest, TResponse>(TRequest, IServiceProvider, CancellationToken,
        //     Func<TRequest, CancellationToken, ValueTask<TResponse>>) -> ValueTask<TResponse>
        private static bool HasServicesHandle(INamedTypeSymbol type)
        {
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var member in current.GetMembers("Handle"))
                {
                    if (member is IMethodSymbol method
                        && method.IsStatic
                        && method.DeclaredAccessibility == Accessibility.Public
                        && IsServicesShape(method))
                        return true;
                }
            }
            return false;
        }

        private static bool IsServicesShape(IMethodSymbol method)
        {
            if (method.TypeParameters.Length != 2 || method.Parameters.Length != 4)
                return false;

            var request = method.TypeParameters[0];
            var response = method.TypeParameters[1];
            var parameters = method.Parameters;

            return SymbolEqualityComparer.Default.Equals(parameters[0].Type, request)
                && IsType(parameters[1].Type, "System.IServiceProvider")
                && IsType(parameters[2].Type, "System.Threading.CancellationToken")
                && parameters[3].Type is INamedTypeSymbol next
                && IsType(next.OriginalDefinition, "System.Func<T1, T2, TResult>")
                && SymbolEqualityComparer.Default.Equals(next.TypeArguments[0], request)
                && IsType(next.TypeArguments[1], "System.Threading.CancellationToken")
                && IsValueTaskOf(next.TypeArguments[2], response)
                && IsValueTaskOf(method.ReturnType, response);
        }

        private static bool IsValueTaskOf(ITypeSymbol type, ITypeParameterSymbol response) =>
            type is INamedTypeSymbol named
            && IsType(named.OriginalDefinition, "System.Threading.Tasks.ValueTask<TResult>")
            && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], response);

        private static bool IsType(ITypeSymbol type, string name) =>
            string.Equals(type.ToDisplayString(), name, StringComparison.Ordinal);

        public bool Equals(BehaviorServicesInfo? other) =>
            other is not null
            && BehaviorTypeName == other.BehaviorTypeName
            && TakesServices == other.TakesServices
            && UsesCurrent == other.UsesCurrent;

        public override bool Equals(object? obj) => Equals(obj as BehaviorServicesInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = BehaviorTypeName.GetHashCode();
                hash = (hash * 31) + (TakesServices ? 1 : 0);
                hash = (hash * 31) + (UsesCurrent ? 1 : 0);
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
        /// The behavior's services info. A behavior this map does not know is treated as one that
        /// reads <c>PipelineBehaviorServices.Current</c>, which is always safe.
        /// </summary>
        public BehaviorServicesInfo Get(string behaviorTypeName) =>
            _byName.TryGetValue(behaviorTypeName, out var info)
                ? info
                : new BehaviorServicesInfo(behaviorTypeName, takesServices: false, usesCurrent: true);

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
