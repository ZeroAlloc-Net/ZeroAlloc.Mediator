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
/// singleton by <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>.
/// The first <c>IMediator</c> resolved from the container constructs it through
/// <see cref="PipelineBehaviorStateActivation"/>; an app that only dispatches through the
/// static <c>Mediator</c> class resolves this accessor, or the activation, once after
/// <c>BuildServiceProvider()</c>.
/// </summary>
/// <remarks>
/// Disposing the container does not clear the state. A request dispatched afterwards throws
/// <see cref="ObjectDisposedException"/> rather than skipping authorization.
/// </remarks>
public sealed class AuthorizationBehaviorAccessor : IPipelineBehaviorStateInitializer
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Stores the provided <paramref name="serviceProvider"/> into <see cref="AuthorizationBehaviorState.ServiceProvider"/>.</summary>
    public AuthorizationBehaviorAccessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        AuthorizationBehaviorState.ServiceProvider = serviceProvider;
    }

    /// <summary>Stores this container's provider into <see cref="AuthorizationBehaviorState.ServiceProvider"/> again.</summary>
    void IPipelineBehaviorStateInitializer.Initialize() =>
        AuthorizationBehaviorState.ServiceProvider = _serviceProvider;
}
#pragma warning restore MA0048
