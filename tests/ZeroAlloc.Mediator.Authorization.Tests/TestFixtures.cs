using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

// AuthorizationBehaviorState.ServiceProvider is mutated by AddMediator().WithAuthorization()
// via AuthorizationBehaviorAccessor — keep these tests non-parallel to avoid cross-test
// service-provider stomping.
[CollectionDefinition("non-parallel-authorization", DisableParallelization = true)]
public sealed class NonParallelAuthorizationCollection { }
