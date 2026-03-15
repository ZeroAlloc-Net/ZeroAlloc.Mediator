#nullable enable
using System;

namespace ZMediator.Generator
{
    internal sealed class RequestHandlerInfo : IEquatable<RequestHandlerInfo>
    {
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }
        public string HandlerTypeName { get; }
        public bool IsRequestValueType { get; }

        public RequestHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool isRequestValueType)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            IsRequestValueType = isRequestValueType;
        }

        public bool Equals(RequestHandlerInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsRequestValueType == other.IsRequestValueType;
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
                return hash;
            }
        }
    }
}
