#nullable enable
using System;

namespace ZeroAlloc.Mediator.Generator
{
    /// <summary>
    /// A concrete notification type declared in this compilation, discovered independently of
    /// whether any handler for it is visible here. Companion to <see cref="RequestTypeInfo"/>.
    /// </summary>
    internal sealed class NotificationTypeInfo : IEquatable<NotificationTypeInfo>
    {
        public string NotificationTypeName { get; }
        public bool IsParallel { get; }

        public NotificationTypeInfo(string notificationTypeName, bool isParallel)
        {
            NotificationTypeName = notificationTypeName;
            IsParallel = isParallel;
        }

        public bool Equals(NotificationTypeInfo? other)
        {
            if (other is null) return false;
            return NotificationTypeName == other.NotificationTypeName
                && IsParallel == other.IsParallel;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as NotificationTypeInfo);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + NotificationTypeName.GetHashCode();
                hash = hash * 31 + IsParallel.GetHashCode();
                return hash;
            }
        }
    }
}
