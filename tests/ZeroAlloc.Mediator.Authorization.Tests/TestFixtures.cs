using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Shared test fixtures — policies, requests, stub handlers, and helper types — used by
// AuthorizationBehaviorTests, AllocationBudgetTests, and IAuthorizedRequestTests. Kept in a
// single file so the ZeroAlloc.Authorization source generator emits one
// AddZeroAllocAuthorization() with all five policies + six AuthorizerFor<TRequest> entries.

[Policy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}

[Policy("Premium")]
public sealed class PremiumPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Premium")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Premium role required"));
}

[Policy("AlwaysAllow")]
public sealed class AlwaysAllowPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(UnitResult<AuthorizationFailure>.Success());
}

[Policy("AlwaysDeny")]
public sealed class AlwaysDenyPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Denied"));
}

[Policy("Cancellable")]
public sealed class CancellablePolicy : IAuthorizationPolicy
{
    public async ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return UnitResult<AuthorizationFailure>.Success();
    }
}

[Policy("IntegrationTest")]
public sealed class IntegrationTestPolicy : IAuthorizationPolicy
{
    // Allow when the security context carries the "Admin" role; deny otherwise.
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "needs Admin"));
}

[Policy("ResourceOwner")]
public sealed class ResourceOwnerPolicy : IAuthorizationPolicy
{
    // Resource-based: allow only when the dispatched ResourceOwnerCommand's
    // UserId matches the caller's identity. Demonstrates the v2.1 contract
    // lit up by AuthorizationBehavior wrapping ctx in
    // ResourceSecurityContextAdapter<TRequest>.
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<ResourceOwnerCommand> rc
                && string.Equals(rc.Resource.UserId, ctx.Id, System.StringComparison.Ordinal)
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.not_owner", "resource owner mismatch"));
}

[Policy("ResourceWrongType")]
public sealed class ResourceWrongTypePolicy : IAuthorizationPolicy
{
    // Intentionally type-checks the WRONG resource type
    // (IResourceSecurityContext<ResourceOwnerCommand>) while being applied to
    // ResourceWrongTypeCommand. The cast must fail at runtime; the deny
    // branch must fire with the "resource.wrong_type" code.
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<ResourceOwnerCommand>
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("resource.wrong_type", "wrong resource type"));
}

// Plain IRequest + [RequirePolicy] → throw path.
[RequirePolicy("AlwaysAllow")]
public sealed record GetThingThrowAllow(int Id) : IRequest<int>;

[RequirePolicy("AlwaysDeny")]
public sealed record GetThingThrowDeny(int Id) : IRequest<int>;

// IAuthorizedRequest → Result path.
[RequirePolicy("AlwaysAllow")]
public sealed record GetThingResultAllow(int Id) : IAuthorizedRequest<int>;

[RequirePolicy("AlwaysDeny")]
public sealed record GetThingResultDeny(int Id) : IAuthorizedRequest<int>;

// Stacked policies (AND). [RequirePolicy("AdminOnly")] is evaluated before [RequirePolicy("Premium")].
[RequirePolicy("AdminOnly")]
[RequirePolicy("Premium")]
public sealed record GetThingAdminPremium(int Id) : IRequest<int>;

[RequirePolicy("Cancellable")]
public sealed record GetThingCancellable(int Id) : IRequest<int>;

// No [RequirePolicy] — for fail-open coverage.
public sealed record GetThingUnauthorized(int Id) : IRequest<int>;

// Integration tests drive this through IMediator.Send. The shim below
// (AuthorizationBehaviorShim) is what the test-assembly's Mediator generator
// picks up to wire AuthorizationBehavior.Handle into the dispatcher; this
// request just exists to be sent.
[RequirePolicy("IntegrationTest")]
public sealed record IntegrationTestRequest(int Value) : IRequest<int>;

[RequirePolicy("ResourceOwner")]
public sealed record ResourceOwnerCommand(string UserId, int Payload) : IRequest<int>;

[RequirePolicy("ResourceWrongType")]
public sealed record ResourceWrongTypeCommand(string UserId) : IRequest<int>;

// Stub handlers — required by ZAM001 (every IRequest<T> needs a registered handler in the
// compilation). Tests drive AuthorizationBehavior.Handle directly, bypassing the dispatcher,
// so the handlers are never invoked.
public sealed class StubGetThingThrowAllowHandler : IRequestHandler<GetThingThrowAllow, int>
{ public ValueTask<int> Handle(GetThingThrowAllow r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class StubGetThingThrowDenyHandler : IRequestHandler<GetThingThrowDeny, int>
{ public ValueTask<int> Handle(GetThingThrowDeny r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class StubGetThingResultAllowHandler : IRequestHandler<GetThingResultAllow, Result<int, AuthorizationFailure>>
{ public ValueTask<Result<int, AuthorizationFailure>> Handle(GetThingResultAllow r, CancellationToken ct)
    => ValueTask.FromResult<Result<int, AuthorizationFailure>>(0); }
public sealed class StubGetThingResultDenyHandler : IRequestHandler<GetThingResultDeny, Result<int, AuthorizationFailure>>
{ public ValueTask<Result<int, AuthorizationFailure>> Handle(GetThingResultDeny r, CancellationToken ct)
    => ValueTask.FromResult<Result<int, AuthorizationFailure>>(0); }
public sealed class StubGetThingAdminPremiumHandler : IRequestHandler<GetThingAdminPremium, int>
{ public ValueTask<int> Handle(GetThingAdminPremium r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class StubGetThingCancellableHandler : IRequestHandler<GetThingCancellable, int>
{ public ValueTask<int> Handle(GetThingCancellable r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class StubGetThingUnauthorizedHandler : IRequestHandler<GetThingUnauthorized, int>
{ public ValueTask<int> Handle(GetThingUnauthorized r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class IntegrationTestHandler : IRequestHandler<IntegrationTestRequest, int>
{
    public ValueTask<int> Handle(IntegrationTestRequest r, CancellationToken ct)
        => ValueTask.FromResult(r.Value * 2);
}
public sealed class StubResourceOwnerHandler : IRequestHandler<ResourceOwnerCommand, int>
{ public ValueTask<int> Handle(ResourceOwnerCommand r, CancellationToken ct) => ValueTask.FromResult(0); }
public sealed class StubResourceWrongTypeHandler : IRequestHandler<ResourceWrongTypeCommand, int>
{ public ValueTask<int> Handle(ResourceWrongTypeCommand r, CancellationToken ct) => ValueTask.FromResult(0); }

internal sealed record TestSecurityContext(string Id,
                                            IReadOnlySet<string> Roles,
                                            IReadOnlyDictionary<string, string> Claims) : ISecurityContext;

internal sealed class TestSecurityContextAccessor : ISecurityContextAccessor
{
    public ISecurityContext Current { get; set; } = AnonymousSecurityContext.Instance;
}

internal static class TestSecurityContexts
{
    public static ISecurityContext With(params string[] roles) =>
        new TestSecurityContext("user-1",
            new HashSet<string>(roles, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
}

// Mutable counter resolved by CountingDownstreamBehavior to record whether
// downstream pipeline behaviors ran on a given Send. AddSingleton in tests.
internal sealed class InvocationCounter { public int Count; }

// Local shim — the test-assembly's Mediator generator sees this in the
// current compilation (cross-assembly AuthorizationBehavior is invisible to
// it). The shim's static Handle just forwards to the real behavior. Same
// Order constant (-1000), identical contract.
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehaviorShim : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => AuthorizationBehavior.Handle<TRequest, TResponse>(request, ct, next);
}

// Numerically AFTER the shim (-500 > -1000): runs only if the shim did NOT
// short-circuit. The integration tests assert this counter to prove
// pipeline ordering took effect.
[PipelineBehavior(Order = -500)]
public sealed class CountingDownstreamBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        // Reads via AuthorizationBehaviorState (now public after Task 1).
        AuthorizationBehaviorState.ServiceProvider!
            .GetRequiredService<InvocationCounter>().Count++;
        return next(request, ct);
    }
}

// AuthorizationBehaviorState.ServiceProvider is mutated by AddMediator().WithAuthorization()
// via AuthorizationBehaviorAccessor — keep these tests non-parallel to avoid cross-test
// service-provider stomping.
[CollectionDefinition("non-parallel-authorization", DisableParallelization = true)]
public sealed class NonParallelAuthorizationCollection { }
