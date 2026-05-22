using System;

namespace ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // file groups behavior-public accessor + state
/// <summary>
/// Static carrier for the active <see cref="IServiceProvider"/> consumed by
/// <see cref="AuthorizationBehavior.Handle{TRequest, TResponse}"/>. Set via
/// <see cref="AuthorizationBehaviorAccessor"/>'s constructor on first DI
/// resolution after <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>.
/// </summary>
/// <remarks>
/// Public so non-Mediator hosts (samples, AOT smoke binaries) can resolve the
/// accessor explicitly instead of writing the field directly — see
/// <see cref="AuthorizationBehaviorAccessor"/>.
/// </remarks>
public static class AuthorizationBehaviorState
{
    /// <summary>The active provider for the authorization behavior, or <see langword="null"/> until first accessor construction.</summary>
#pragma warning disable MA0069 // public mutable static is by-design: set by AuthorizationBehaviorAccessor ctor side-effect
    public static volatile IServiceProvider? ServiceProvider;
#pragma warning restore MA0069
}

/// <summary>
/// DI-resolved hook whose constructor side-effects
/// <see cref="AuthorizationBehaviorState.ServiceProvider"/>. Registered as a
/// singleton by <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>;
/// resolve it once after <c>BuildServiceProvider()</c> to initialise the
/// behavior's view of the container.
/// </summary>
public sealed class AuthorizationBehaviorAccessor
{
    /// <summary>Stores the provided <paramref name="serviceProvider"/> into <see cref="AuthorizationBehaviorState.ServiceProvider"/>.</summary>
    public AuthorizationBehaviorAccessor(IServiceProvider serviceProvider) =>
        AuthorizationBehaviorState.ServiceProvider = serviceProvider;
}
#pragma warning restore MA0048
