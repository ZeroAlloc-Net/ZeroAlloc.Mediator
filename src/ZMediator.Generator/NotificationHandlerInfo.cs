#nullable enable
using System;

namespace ZMediator.Generator
{
    internal sealed class NotificationHandlerInfo : IEquatable<NotificationHandlerInfo>
    {
        public string NotificationTypeName { get; }
        public string HandlerTypeName { get; }
        public bool IsParallel { get; }
        public bool IsBaseHandler { get; }
        /// <summary>
        /// Semicolon-delimited fully-qualified names of all INotification-derived
        /// interfaces the notification type implements. Empty for base handlers.
        /// </summary>
        public string BaseNotificationTypeNames { get; }

        public NotificationHandlerInfo(
            string notificationTypeName,
            string handlerTypeName,
            bool isParallel,
            bool isBaseHandler,
            string baseNotificationTypeNames)
        {
            NotificationTypeName = notificationTypeName;
            HandlerTypeName = handlerTypeName;
            IsParallel = isParallel;
            IsBaseHandler = isBaseHandler;
            BaseNotificationTypeNames = baseNotificationTypeNames;
        }

        public bool Equals(NotificationHandlerInfo? other)
        {
            if (other is null) return false;
            return NotificationTypeName == other.NotificationTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsParallel == other.IsParallel
                && IsBaseHandler == other.IsBaseHandler
                && BaseNotificationTypeNames == other.BaseNotificationTypeNames;
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
                hash = hash * 31 + IsBaseHandler.GetHashCode();
                hash = hash * 31 + BaseNotificationTypeNames.GetHashCode();
                return hash;
            }
        }
    }
}
