using System;
using System.Diagnostics.CodeAnalysis;
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
///         <see cref="IAuthorizedRequest{TResponse}"/>) ⇒ returns
///         <c>Result&lt;T, AuthorizationFailure&gt;.Failure(...)</c>.</item>
/// </list>
/// Order <c>-1000</c> so authorization runs before validation/cache/resilience.</para>
/// </remarks>
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehavior : IPipelineBehavior
{
    // IL2091: AuthorizationFailureFactory<TResponse> requires TResponse to satisfy
    // [DynamicallyAccessedMembers(PublicMethods)] so the trimmer preserves Result<,>.Failure(...).
    // Handle's TResponse can't carry that annotation without forcing every consumer of IPipelineBehavior
    // to declare it. Safe to suppress because FailureFactory does a runtime shape check and only
    // proceeds when TResponse is exactly Result<T, AuthorizationFailure>; otherwise Create is null and
    // we fall through to the throw path that doesn't touch TResponse reflectively.
    [UnconditionalSuppressMessage("Trimming", "IL2091:Target generic argument does not satisfy DynamicallyAccessedMemberTypes",
        Justification = "FailureFactory does a runtime shape check; Result<,> is in a referenced assembly and its Failure method is preserved by trimming roots.")]
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var sp = AuthorizationBehaviorState.ServiceProvider;
        if (sp is null)
            return await next(request, ct).ConfigureAwait(false);

        var authorizer = sp.GetService<AuthorizerFor<TRequest>>();
        if (authorizer is null)
            return await next(request, ct).ConfigureAwait(false);

        // Resolve the security context once per request. AuthorizationOptions ensures exactly
        // one ISecurityContext source is registered (UseAnonymous / UseFactory / UseAccessor).
        var ctx = sp.GetService<ISecurityContext>();
        if (ctx is null)
        {
            throw new InvalidOperationException(
                "AuthorizationBehavior could not resolve ISecurityContext. Ensure WithAuthorization() " +
                "configured a security-context source (UseSecurityContextFactory / UseAnonymousSecurityContext / UseAccessor<>).");
        }

        var result = await authorizer.EvaluateAsync(ctx, ct).ConfigureAwait(false);
        if (result.IsSuccess)
            return await next(request, ct).ConfigureAwait(false);

        var failure = result.Error;
        var factory = AuthorizationFailureFactory<TResponse>.Create;
        if (factory is not null)
            return factory(failure);

        throw new AuthorizationDeniedException(failure);
    }
}
