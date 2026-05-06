using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Pipeline behavior that evaluates the policies declared via <see cref="AuthorizeAttribute"/>
/// on the request type before dispatching the request to its handler. On deny:
/// <list type="bullet">
///   <item>plain <see cref="IRequest{TResponse}"/> + <c>[Authorize]</c> ⇒ throws
///         <see cref="AuthorizationDeniedException"/>;</item>
///   <item><see cref="IAuthorizedRequest{TResponse}"/> ⇒ returns
///         <c>Result&lt;TValue, AuthorizationFailure&gt;.Failure(...)</c>.</item>
/// </list>
/// Order is <c>-1000</c> so it runs before validation/cache/resilience behaviors.
/// </summary>
/// <remarks>
/// <para><b>Why a single class instead of two?</b> The <c>ZeroAlloc.Pipeline</c> contract
/// requires every behavior to expose exactly one static <c>Handle&lt;TRequest, TResponse&gt;</c>
/// method on the class — the source generator that builds the dispatcher pulls that signature
/// out of the type. Splitting throw/Result paths into two classes would mean two distinct
/// entries in the generated pipeline (one per closed TResponse shape), which the framework
/// doesn't model. So we inspect the closed TResponse here and dispatch on shape.</para>
/// <para><b>Allocation profile:</b> the per-TRequest policy-name array is cached in
/// <c>RequestPolicies&lt;TRequest&gt;.Names</c>; the FailureFactory builder is cached per
/// closed TResponse. The hot allow path is one interface dispatch into the policy plus the
/// downstream <c>next(...)</c> call — zero allocations beyond the policy itself.</para>
/// </remarks>
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehavior : IPipelineBehavior
{
    // IL2091: AuthorizationFailureFactory<TResponse> requires TResponse to satisfy
    // [DynamicallyAccessedMembers(PublicMethods)] so the trimmer preserves Result<,>.Failure(...).
    // The Handle method's TResponse can't carry that annotation without forcing every consumer of
    // IPipelineBehavior to declare it too. Safe to suppress because FailureFactory does a runtime
    // shape check (typeof(TResponse).IsGenericType / GenericTypeDefinition == typeof(Result<,>) /
    // typeArgs[1] == typeof(AuthorizationFailure)) and only proceeds when TResponse is exactly
    // Result<T, AuthorizationFailure>; otherwise Create is null and we fall through to the throw
    // path that doesn't touch TResponse reflectively. Result<,> lives in ZeroAlloc.Results which is
    // referenced by this assembly, so its public methods are kept by the trimmer regardless.
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

        var policyNames = RequestPolicies<TRequest>.Names;
        if (policyNames.Length == 0)
            return await next(request, ct).ConfigureAwait(false);

        // Resolve the security context once per request. The runtime registers exactly one
        // ISecurityContext via UseAnonymous / UseFactory / UseAccessor — see AuthorizationOptions.
        var ctx = sp.GetService<ISecurityContext>();
        if (ctx is null)
        {
            throw new InvalidOperationException(
                "AuthorizationBehavior could not resolve ISecurityContext. Ensure WithAuthorization() " +
                "configured a security-context source (UseSecurityContextFactory / UseAnonymousSecurityContext / UseAccessor<>).");
        }

        var resolve = MediatorAuthorizationGeneratedHooks.ResolvePolicy;
        if (resolve is null)
        {
            // No policies emitted — but a request had policy names? Indicates a generator
            // bug or partial compilation. Fail loudly rather than silently allow.
            throw new InvalidOperationException(
                "AuthorizationBehavior found policy names for a request but the generator did not emit a policy resolver. " +
                "This indicates the source generator did not run; rebuild the consuming project.");
        }

        // Iterate policies in source-declaration order — first-deny short-circuits (AND semantics).
        // for-loop preferred here because the body uses index-based access pattern; foreach over
        // a string[] would inflate IL with an enumerator pattern under analyzer pressure.
#pragma warning disable HLQ013
        for (var i = 0; i < policyNames.Length; i++)
#pragma warning restore HLQ013
        {
            ct.ThrowIfCancellationRequested();
            var policy = resolve(policyNames[i], sp);
            var result = await policy.EvaluateAsync(ctx, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                var failure = result.Error;
                var factory = AuthorizationFailureFactory<TResponse>.Create;
                if (factory is not null)
                    return factory(failure);
                throw new AuthorizationDeniedException(failure);
            }
        }

        return await next(request, ct).ConfigureAwait(false);
    }

    // Per-TRequest static cache of the emitted policy-name array. Populated lazily on first
    // touch of the closed generic type by calling the hook delegate; subsequent dispatches
    // read the array directly with no allocation.
    private static class RequestPolicies<TRequest>
    {
        public static readonly string[] Names =
            MediatorAuthorizationGeneratedHooks.GetPoliciesForRequestType(typeof(TRequest));
    }
}

