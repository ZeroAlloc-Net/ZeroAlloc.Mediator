#nullable enable
using System;

namespace ZMediator.Generator
{
    internal sealed class RequestTypeInfo : IEquatable<RequestTypeInfo>
    {
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }

        public RequestTypeInfo(string requestTypeName, string responseTypeName)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
        }

        public bool Equals(RequestTypeInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RequestTypeInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + RequestTypeName.GetHashCode();
                hash = hash * 31 + ResponseTypeName.GetHashCode();
                return hash;
            }
        }
    }
}
