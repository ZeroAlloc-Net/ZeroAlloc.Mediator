#nullable enable
using System;

namespace ZeroAlloc.Mediator.Generator
{
    internal sealed class RequestHandlerInfo : IEquatable<RequestHandlerInfo>
    {
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }
        public string HandlerTypeName { get; }
        public bool IsRequestValueType { get; }
        public bool HasParameterlessConstructor { get; }
        /// <summary>
        /// Source location of the handler class identifier, used to scope
        /// ZAM002, ZAM003 and ZAM008 so <c>#pragma warning disable</c> and
        /// <c>[SuppressMessage]</c> can target the offending handler.
        /// Part of equality, so a cached model never keeps a stale location. The emitted
        /// source is built from <see cref="WithoutLocation"/>, so a moved handler does not
        /// regenerate it.
        /// </summary>
        public LocationInfo? HandlerLocation { get; }

        /// <summary>
        /// The ServiceLifetime value from [HandlerLifetime] on the handler class, or null when the attribute is absent.
        /// </summary>
        public int? Lifetime { get; }

        /// <summary>
        /// Whether the handler class is abstract. An abstract handler still takes part in dispatch
        /// and diagnostics, but cannot be instantiated, so it is not registered in the container.
        /// </summary>
        public bool IsAbstract { get; }

        public RequestHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool isRequestValueType, bool hasParameterlessConstructor, LocationInfo? handlerLocation, int? lifetime, bool isAbstract)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            IsRequestValueType = isRequestValueType;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
            Lifetime = lifetime;
            IsAbstract = isAbstract;
        }

        /// <summary>This model without its location, for the emitted source.</summary>
        public RequestHandlerInfo WithoutLocation() =>
            HandlerLocation is null
                ? this
                : new RequestHandlerInfo(RequestTypeName, ResponseTypeName, HandlerTypeName, IsRequestValueType, HasParameterlessConstructor, null, Lifetime, IsAbstract);

        public bool Equals(RequestHandlerInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsRequestValueType == other.IsRequestValueType
                && HasParameterlessConstructor == other.HasParameterlessConstructor
                && Equals(HandlerLocation, other.HandlerLocation)
                && Lifetime == other.Lifetime
                && IsAbstract == other.IsAbstract;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RequestHandlerInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + RequestTypeName.GetHashCode();
                hash = hash * 31 + ResponseTypeName.GetHashCode();
                hash = hash * 31 + HandlerTypeName.GetHashCode();
                hash = hash * 31 + IsRequestValueType.GetHashCode();
                hash = hash * 31 + HasParameterlessConstructor.GetHashCode();
                hash = hash * 31 + (HandlerLocation?.GetHashCode() ?? 0);
                hash = hash * 31 + (Lifetime ?? -1);
                hash = hash * 31 + IsAbstract.GetHashCode();
                return hash;
            }
        }
    }
}
