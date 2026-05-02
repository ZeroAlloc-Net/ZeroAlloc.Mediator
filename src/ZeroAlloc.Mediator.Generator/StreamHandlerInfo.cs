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

        public StreamHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool hasParameterlessConstructor)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            HasParameterlessConstructor = hasParameterlessConstructor;
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
