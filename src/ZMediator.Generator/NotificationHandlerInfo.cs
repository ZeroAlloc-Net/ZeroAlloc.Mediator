#nullable enable
using System;

namespace ZMediator.Generator
{
    internal sealed class NotificationHandlerInfo : IEquatable<NotificationHandlerInfo>
    {
        public string NotificationTypeName { get; }
        public string HandlerTypeName { get; }
        public bool IsParallel { get; }

        public NotificationHandlerInfo(string notificationTypeName, string handlerTypeName, bool isParallel)
        {
            NotificationTypeName = notificationTypeName;
            HandlerTypeName = handlerTypeName;
            IsParallel = isParallel;
        }

        public bool Equals(NotificationHandlerInfo? other)
        {
            if (other is null) return false;
            return NotificationTypeName == other.NotificationTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsParallel == other.IsParallel;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as NotificationHandlerInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + NotificationTypeName.GetHashCode();
                hash = hash * 31 + HandlerTypeName.GetHashCode();
                hash = hash * 31 + IsParallel.GetHashCode();
                return hash;
            }
        }
    }
}
