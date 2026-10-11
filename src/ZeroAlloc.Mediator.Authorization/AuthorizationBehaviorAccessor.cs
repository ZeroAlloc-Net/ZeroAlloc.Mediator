using System;

namespace ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // file groups behavior-public accessor + state
/// <summary>
/// Static carrier for the container <see cref="AuthorizationBehavior.Handle{TRequest, TResponse}"/>
/// falls back to when a request is not dispatched through <c>IMediator</c>, such as through the
/// static <c>Mediator</c> class. Set via <see cref="AuthorizationBehaviorAccessor"/>'s constructor
/// on first DI resolution after <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>.
/// </summary>
/// <remarks>
/// <para>A request dispatched through <c>IMediator</c> never reads this field: its services come
/// from <see cref="PipelineBehaviorServices.Current"/>, the provider of the dispatching
/// <c>IMediator</c>. Without one, the behavior opens a scope on this container for each request,
/// and never resolves the scoped security context from the container itself.</para>
/// <para>Public so non-Mediator hosts (samples, AOT smoke binaries) can resolve the
/// accessor explicitly instead of writing the field directly — see
/// <see cref="AuthorizationBehaviorAccessor"/>.</para>
/// </remarks>
public static class AuthorizationBehaviorState
{
    /// <summary>The container for requests dispatched without <c>IMediator</c>, or <see langword="null"/> until first accessor construction.</summary>
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
/// Disposing the container does not clear the state. A request dispatched afterwards without
/// <c>IMediator</c> throws <see cref="ObjectDisposedException"/> rather than skipping authorization.
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
