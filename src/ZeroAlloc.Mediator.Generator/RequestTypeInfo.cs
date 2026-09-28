#nullable enable
using System;

namespace ZeroAlloc.Mediator.Generator
{
    internal sealed class RequestTypeInfo : IEquatable<RequestTypeInfo>
    {
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }

        /// <summary>
        /// The request type's identifier in this declaration, for ZAM001 and ZAM003. Part of
        /// equality, so a cached model never keeps a stale location.
        /// </summary>
        public LocationInfo? Location { get; }

        public RequestTypeInfo(string requestTypeName, string responseTypeName, LocationInfo? location)
        {
            RequestTypeName = requestTypeName;
            ResponseTypeName = responseTypeName;
            Location = location;
        }

        public bool Equals(RequestTypeInfo? other)
        {
            if (other is null) return false;
            return RequestTypeName == other.RequestTypeName
                && ResponseTypeName == other.ResponseTypeName
                && Equals(Location, other.Location);
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
                hash = hash * 31 + (Location?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
