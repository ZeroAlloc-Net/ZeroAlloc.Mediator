#nullable enable
using System;
using Microsoft.CodeAnalysis;

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
        /// ZAM008 (and friends) so <c>#pragma warning disable</c> and
        /// <c>[SuppressMessage]</c> can target the offending handler.
        /// Excluded from <see cref="Equals(RequestHandlerInfo?)"/> /
        /// <see cref="GetHashCode"/> so trivial source movements do not
        /// invalidate the incremental generator cache.
        /// </summary>
        public Location? HandlerLocation { get; }

        public RequestHandlerInfo(string requestTypeName, string responseTypeName, string handlerTypeName, bool isRequestValueType, bool hasParameterlessConstructor, Location? handlerLocation)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            HandlerTypeName = handlerTypeName;
            IsRequestValueType = isRequestValueType;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
        }

        public bool Equals(RequestHandlerInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsRequestValueType == other.IsRequestValueType
                && HasParameterlessConstructor == other.HasParameterlessConstructor;
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
                return hash;
            }
        }
    }
}
