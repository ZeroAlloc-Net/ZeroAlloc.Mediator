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

        /// <summary>
        /// The ServiceLifetime value the handler class asks for through [HandlerLifetime] or a
        /// ZeroAlloc.Inject lifetime attribute, or null when it carries neither.
        /// </summary>
        public int? Lifetime { get; }

        /// <summary>
        /// Whether the handler class is abstract. An abstract handler still takes part in dispatch
        /// and diagnostics, but cannot be instantiated, so it is not registered in the container.
        /// </summary>
        public bool IsAbstract { get; }

        /// <summary>
        /// Whether the handler class has at least one public instance constructor. Microsoft DI
        /// builds a type only through a public constructor, so a handler without one is not
        /// registered in the container.
        /// </summary>
        public bool HasPublicConstructor { get; }

        public NotificationHandlerInfo(
            string notificationTypeName,
            string handlerTypeName,
            bool isParallel,
            bool isBaseHandler,
            string baseNotificationTypeNames,
            bool hasParameterlessConstructor,
            LocationInfo? handlerLocation,
            int? lifetime, bool isAbstract, bool hasPublicConstructor)
        {
            NotificationTypeName = notificationTypeName;
            HandlerTypeName = handlerTypeName;
            IsParallel = isParallel;
            IsBaseHandler = isBaseHandler;
            BaseNotificationTypeNames = baseNotificationTypeNames;
            HasParameterlessConstructor = hasParameterlessConstructor;
            HandlerLocation = handlerLocation;
            Lifetime = lifetime;
            IsAbstract = isAbstract;
            HasPublicConstructor = hasPublicConstructor;
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
                && Equals(HandlerLocation, other.HandlerLocation)
                && Lifetime == other.Lifetime
                && IsAbstract == other.IsAbstract
                && HasPublicConstructor == other.HasPublicConstructor;
        }

        /// <summary>This model without its location, for the emitted source.</summary>
        public NotificationHandlerInfo WithoutLocation() =>
            HandlerLocation is null
                ? this
                : new NotificationHandlerInfo(NotificationTypeName, HandlerTypeName, IsParallel, IsBaseHandler, BaseNotificationTypeNames, HasParameterlessConstructor, null, Lifetime, IsAbstract, HasPublicConstructor);

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
                hash = hash * 31 + (Lifetime ?? -1);
                hash = hash * 31 + IsAbstract.GetHashCode();
                hash = hash * 31 + HasPublicConstructor.GetHashCode();
                return hash;
            }
        }
    }
}
