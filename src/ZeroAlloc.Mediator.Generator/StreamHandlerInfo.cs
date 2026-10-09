#nullable enable
using System;

namespace ZeroAlloc.Mediator.Generator
{
    internal sealed class StreamHandlerInfo : IEquatable<StreamHandlerInfo>
    {
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }
        public string HandlerTypeName { get; }
        public bool HasParameterlessConstructor { get; }
        /// <summary>
        /// Source location of the handler class identifier. See
        /// <see cref="RequestHandlerInfo.HandlerLocation"/>.
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

        public StreamHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool hasParameterlessConstructor, LocationInfo? handlerLocation, int? lifetime, bool isAbstract)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
            Lifetime = lifetime;
            IsAbstract = isAbstract;
        }

        public bool Equals(StreamHandlerInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && HandlerTypeName == other.HandlerTypeName
                && HasParameterlessConstructor == other.HasParameterlessConstructor
                && Equals(HandlerLocation, other.HandlerLocation)
                && Lifetime == other.Lifetime
                && IsAbstract == other.IsAbstract;
        }

        /// <summary>This model without its location, for the emitted source.</summary>
        public StreamHandlerInfo WithoutLocation() =>
            HandlerLocation is null
                ? this
                : new StreamHandlerInfo(RequestTypeName, ResponseTypeName, HandlerTypeName, HasParameterlessConstructor, null, Lifetime, IsAbstract);

        public override bool Equals(object? obj)
        {
            return Equals(obj as StreamHandlerInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + RequestTypeName.GetHashCode();
                hash = hash * 31 + ResponseTypeName.GetHashCode();
                hash = hash * 31 + HandlerTypeName.GetHashCode();
                hash = hash * 31 + HasParameterlessConstructor.GetHashCode();
                hash = hash * 31 + (HandlerLocation?.GetHashCode() ?? 0);
                hash = hash * 31 + (Lifetime ?? -1);
                hash = hash * 31 + IsAbstract.GetHashCode();
                return hash;
            }
        }
    }
}
