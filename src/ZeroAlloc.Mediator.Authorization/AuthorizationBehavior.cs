using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Pipeline behavior that authorizes a request via the source-generated
/// <see cref="AuthorizerFor{TRequest}"/> dispatcher emitted by ZeroAlloc.Authorization v2's
/// bundled generator. Resolves an <see cref="ISecurityContext"/> from the source configured in
/// <see cref="AuthorizationOptions"/>, then calls <see cref="AuthorizerFor{TRequest}.EvaluateAsync"/>.
/// </summary>
/// <remarks>
/// <para><b>Services come from the request being dispatched.</b> The dispatcher and the security
/// context are scoped, so they are resolved from the provider of the <c>IMediator</c> dispatching
/// the request, which is the request's scope when <c>IMediator</c> was resolved from one. The
/// generated <c>IMediator.Send</c> passes that provider to the <c>Handle</c> overload that takes
/// one; the other overload, called by older generators, reads it from
/// <see cref="PipelineBehaviorServices.Current"/>. Each request is authorized with its own
/// caller's identity, and two containers in one process never see each other's services.</para>
/// <para>A request dispatched without <c>IMediator</c>, through the static <c>Mediator</c> class or
/// a direct call, has no request scope. The behavior then opens a scope of its own on the
/// container <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/> was
/// wired to, for that request alone, and only when the request type has a dispatcher. It never
/// resolves the scoped services from the root provider, which would cache the first caller's
/// identity for every later request.</para>
/// <para><b>Fail-open if no dispatcher is registered.</b> When
/// <c>sp.GetService&lt;AuthorizerFor&lt;TRequest&gt;&gt;()</c> returns <see langword="null"/>,
/// the behavior simply forwards to <c>next</c>. This mirrors <c>Mediator.Validation</c>'s
/// "no validator registered ⇒ pass through" pattern; the D3 startup guard in
/// <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/> already
/// ensures <c>AddZeroAllocAuthorization()</c> was called, so a missing per-request dispatcher
/// means the request type has no <c>[RequirePolicy]</c> attributes.</para>
/// <para><b>Deny semantics:</b>
/// <list type="bullet">
///   <item>plain <see cref="IRequest{TResponse}"/> ⇒ throws <see cref="AuthorizationDeniedException"/>;</item>
///   <item><c>Result&lt;T, AuthorizationFailure&gt;</c>-shaped response (incl.
///         <see cref="IAuthorizedRequest{TResponse}"/>) or <c>UnitResult&lt;AuthorizationFailure&gt;</c> ⇒
///         returns a failed result carrying the <see cref="AuthorizationFailure"/>.</item>
/// </list>
/// Order <c>-1000</c> so authorization runs before validation/cache/resilience.</para>
/// <para><b>Streams:</b> pipeline behaviors do not wrap <c>CreateStream</c>, so a stream request
/// is authorized by <see cref="CheckStream{TRequest}"/> instead, which the generated
/// <c>CreateStream</c> calls before the stream handler. A denial throws
/// <see cref="AuthorizationDeniedException"/> when enumeration starts.</para>
/// </remarks>
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var services = PipelineBehaviorServices.Current;
        return services is not null
            ? AuthorizeAsync(services, request, ct, next)
            : AuthorizeInOwnScopeAsync(request, ct, next);
    }

    /// <summary>
    /// Authorizes <paramref name="request"/> with the services of the request being dispatched.
    /// The generated <c>IMediator.Send</c> calls this overload with the provider it was resolved
    /// from, so the pipeline needs no <see cref="PipelineBehaviorServices.Current"/>.
    /// </summary>
    /// <param name="request">The request to authorize.</param>
    /// <param name="services">
    /// The provider of the request being dispatched. The dispatcher and the security context are
    /// resolved from it.
    /// </param>
    /// <param name="ct">The cancellation token of the request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        IServiceProvider services,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        return AuthorizeAsync(services, request, ct, next);
    }

    /// <summary>
    /// Authorizes a stream request before its handler runs. The generated <c>CreateStream</c>
    /// calls this first, when enumeration starts, and a denial throws
    /// <see cref="AuthorizationDeniedException"/> before the handler is called and before any
    /// item is yielded. Pipeline behaviors do not wrap streams, so this is how
    /// <c>[RequirePolicy]</c> applies to a type that implements <c>IStreamRequest&lt;T&gt;</c>.
    /// </summary>
    /// <typeparam name="TRequest">The stream request type.</typeparam>
    /// <param name="request">The stream request to authorize.</param>
    /// <param name="services">
    /// The provider of the request being dispatched, which the generated <c>IMediator</c> passes.
    /// When <see langword="null"/>, as from the static <c>Mediator</c> class, the provider in
    /// <see cref="PipelineBehaviorServices.Current"/> is used, and without one the check runs in
    /// a scope of its own, as for <c>Send</c>.
    /// </param>
    /// <param name="ct">The cancellation token of the enumeration.</param>
    /// <returns>A task that completes when the request is allowed.</returns>
    /// <exception cref="AuthorizationDeniedException">A policy denied the request.</exception>
    public static ValueTask CheckStream<TRequest>(TRequest request, IServiceProvider? services, CancellationToken ct)
    {
        services ??= PipelineBehaviorServices.Current;
        return services is not null
            ? AuthorizeStreamAsync(services, request, ct)
            : AuthorizeStreamInOwnScopeAsync(request, ct);
    }

    // No IMediator dispatch, so no request scope to read. The container WithAuthorization was
    // wired to is the root provider, and scoped services resolved from it are cached for the
    // container's lifetime, so the request gets a scope of its own instead.
    private static async ValueTask<TResponse> AuthorizeInOwnScopeAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var root = AuthorizationBehaviorState.ServiceProvider;
        if (root is null)
            return await next(request, ct).ConfigureAwait(false);

        // A request type without a dispatcher has no policies: skip opening a scope for it.
        // IServiceProviderIsService answers from the registrations without creating anything.
        if (root.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(AuthorizerFor<TRequest>)))
            return await next(request, ct).ConfigureAwait(false);

        var scope = root.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await AuthorizeAsync(scope.ServiceProvider, request, ct, next).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResponse> AuthorizeAsync<TRequest, TResponse>(
        IServiceProvider services,
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        if (await EvaluateAsync(services, request, ct).ConfigureAwait(false) is not { } failure)
            return await next(request, ct).ConfigureAwait(false);

        var factory = AuthorizationFailureFactory<TResponse>.Create;
        if (factory is not null)
            return factory(failure);

        throw new AuthorizationDeniedException(failure);
    }

    // Streams: CreateStream returns the items, not a response to wrap a failure in, so a denial
    // always throws AuthorizationDeniedException, as for a plain IRequest. The same dispatcher
    // and security context decide, so a policy cannot pass on Send and fail on a stream, or the
    // reverse.
    private static async ValueTask AuthorizeStreamAsync<TRequest>(
        IServiceProvider services,
        TRequest request,
        CancellationToken ct)
    {
        if (await EvaluateAsync(services, request, ct).ConfigureAwait(false) is { } failure)
            throw new AuthorizationDeniedException(failure);
    }

    private static async ValueTask AuthorizeStreamInOwnScopeAsync<TRequest>(
        TRequest request,
        CancellationToken ct)
    {
        var root = AuthorizationBehaviorState.ServiceProvider;
        if (root is null)
            return;

        if (root.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(AuthorizerFor<TRequest>)))
            return;

        var scope = root.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await AuthorizeStreamAsync(scope.ServiceProvider, request, ct).ConfigureAwait(false);
        }
    }

    // The decision, shared by Send and streams. Null when the request is allowed, or has no
    // dispatcher and so no policies.
    private static async ValueTask<AuthorizationFailure?> EvaluateAsync<TRequest>(
        IServiceProvider services,
        TRequest request,
        CancellationToken ct)
    {
        var authorizer = services.GetService<AuthorizerFor<TRequest>>();
        if (authorizer is null)
            return null;

        // Resolve the security context once per request. AuthorizationOptions ensures exactly
        // one ISecurityContext source is registered (UseAnonymous / UseFactory / UseAccessor<>).
        var ctx = services.GetService<ISecurityContext>();
        if (ctx is null)
        {
            throw new InvalidOperationException(
                "AuthorizationBehavior could not resolve ISecurityContext. Ensure WithAuthorization() " +
                "configured a security-context source (UseSecurityContextFactory / UseAnonymousSecurityContext / UseAccessor<>).");
        }

        // Wrap the resolved caller-identity context so resource-based policies
        // (ctx is IResourceSecurityContext<TRequest> rc) light up automatically.
        // One allocation per dispatch: 32 B for a reference-type or small struct request.
        var resourceCtx = new ResourceSecurityContextAdapter<TRequest>(ctx, request);
        var result = await authorizer.EvaluateAsync(resourceCtx, ct).ConfigureAwait(false);
        return result.IsSuccess ? null : result.Error;
    }
}
