#nullable enable
using System;

namespace ZeroAlloc.Mediator.Generator
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
        public bool HasParameterlessConstructor { get; }
        /// <summary>
        /// Source location of the handler class identifier. See
        /// <see cref="RequestHandlerInfo.HandlerLocation"/>.
        /// </summary>
        public LocationInfo? HandlerLocation { get; }

        public NotificationHandlerInfo(
            string notificationTypeName,
            string handlerTypeName,
            bool isParallel,
            bool isBaseHandler,
            string baseNotificationTypeNames,
            bool hasParameterlessConstructor,
            LocationInfo? handlerLocation)
        {
            NotificationTypeName = notificationTypeName;
            HandlerTypeName = handlerTypeName;
            IsParallel = isParallel;
            IsBaseHandler = isBaseHandler;
            BaseNotificationTypeNames = baseNotificationTypeNames;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
        }

        public bool Equals(NotificationHandlerInfo? other)
        {
            if (other is null) return false;
            return NotificationTypeName == other.NotificationTypeName
                && HandlerTypeName == other.HandlerTypeName
                && IsParallel == other.IsParallel
                && IsBaseHandler == other.IsBaseHandler
                && BaseNotificationTypeNames == other.BaseNotificationTypeNames
                && HasParameterlessConstructor == other.HasParameterlessConstructor
                && Equals(HandlerLocation, other.HandlerLocation);
        }

        /// <summary>This model without its location, for the emitted source.</summary>
        public NotificationHandlerInfo WithoutLocation() =>
            HandlerLocation is null
                ? this
                : new NotificationHandlerInfo(NotificationTypeName, HandlerTypeName, IsParallel, IsBaseHandler, BaseNotificationTypeNames, HasParameterlessConstructor, null);

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
                hash = hash * 31 + HasParameterlessConstructor.GetHashCode();
                hash = hash * 31 + (HandlerLocation?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
