#nullable enable
using System;
using Microsoft.CodeAnalysis;

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
        /// Excluded from equality so source-position changes do not bust
        /// the incremental cache.
        /// </summary>
        public Location? HandlerLocation { get; }

        public StreamHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool hasParameterlessConstructor, Location? handlerLocation)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
        }

        public bool Equals(StreamHandlerInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && HandlerTypeName == other.HandlerTypeName
                && HasParameterlessConstructor == other.HasParameterlessConstructor;
        }

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
                return hash;
            }
        }
    }
}
