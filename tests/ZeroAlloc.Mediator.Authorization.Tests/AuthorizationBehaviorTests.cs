using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Policy definitions discovered by the source generator for this test project.
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        ctx.Roles.Contains("Admin", StringComparer.Ordinal);
}

[AuthorizationPolicy("Premium")]
public sealed class PremiumPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        ctx.Roles.Contains("Premium", StringComparer.Ordinal);
}

[AuthorizationPolicy("AlwaysAllow")]
public sealed class AlwaysAllowPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;
}

[AuthorizationPolicy("AlwaysDeny")]
public sealed class AlwaysDenyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => false;
}

[AuthorizationPolicy("Cancellable")]
public sealed class CancellablePolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => true;

    public async ValueTask<bool> IsAuthorizedAsync(ISecurityContext ctx, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return true;
    }
}

// Plain IRequest + [Authorize] → throw path.
[Authorize("AlwaysAllow")]
public sealed record GetThingThrowAllow(int Id) : IRequest<int>;

[Authorize("AlwaysDeny")]
public sealed record GetThingThrowDeny(int Id) : IRequest<int>;

// IAuthorizedRequest → Result path.
[Authorize("AlwaysAllow")]
public sealed record GetThingResultAllow(int Id) : IAuthorizedRequest<int>;

[Authorize("AlwaysDeny")]
public sealed record GetThingResultDeny(int Id) : IAuthorizedRequest<int>;

// Stacked policies (AND).
[Authorize("AdminOnly")]
[Authorize("Premium")]
public sealed record GetThingAdminPremium(int Id) : IRequest<int>;

[Authorize("Cancellable")]
public sealed record GetThingCancellable(int Id) : IRequest<int>;

// No [Authorize] — for ordering/no-op test.
public sealed record GetThingUnauthorized(int Id) : IRequest<int>;

// Stub handlers — exist only to satisfy ZAM001 (every IRequest<T> needs a registered
// handler). Tests bypass the dispatcher by calling AuthorizationBehavior.Handle directly,
// so these are never invoked.
public sealed class StubGetThingThrowAllowHandler : IRequestHandler<GetThingThrowAllow, int>
{
    public ValueTask<int> Handle(GetThingThrowAllow r, CancellationToken ct) => ValueTask.FromResult(0);
}
public sealed class StubGetThingThrowDenyHandler : IRequestHandler<GetThingThrowDeny, int>
{
    public ValueTask<int> Handle(GetThingThrowDeny r, CancellationToken ct) => ValueTask.FromResult(0);
}
public sealed class StubGetThingResultAllowHandler : IRequestHandler<GetThingResultAllow, Result<int, AuthorizationFailure>>
{
    public ValueTask<Result<int, AuthorizationFailure>> Handle(GetThingResultAllow r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, AuthorizationFailure>>(0);
}
public sealed class StubGetThingResultDenyHandler : IRequestHandler<GetThingResultDeny, Result<int, AuthorizationFailure>>
{
    public ValueTask<Result<int, AuthorizationFailure>> Handle(GetThingResultDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, AuthorizationFailure>>(0);
}
public sealed class StubGetThingAdminPremiumHandler : IRequestHandler<GetThingAdminPremium, int>
{
    public ValueTask<int> Handle(GetThingAdminPremium r, CancellationToken ct) => ValueTask.FromResult(0);
}
public sealed class StubGetThingCancellableHandler : IRequestHandler<GetThingCancellable, int>
{
    public ValueTask<int> Handle(GetThingCancellable r, CancellationToken ct) => ValueTask.FromResult(0);
}
public sealed class StubGetThingUnauthorizedHandler : IRequestHandler<GetThingUnauthorized, int>
{
    public ValueTask<int> Handle(GetThingUnauthorized r, CancellationToken ct) => ValueTask.FromResult(0);
}

// Tests share AuthorizationBehaviorState.ServiceProvider — disable parallelization.
[CollectionDefinition("non-parallel-authorization", DisableParallelization = true)]
public sealed class NonParallelAuthorizationCollection { }

[Collection("non-parallel-authorization")]
public sealed class AuthorizationBehaviorTests : IDisposable
{
    public AuthorizationBehaviorTests()
    {
        AuthorizationBehaviorState.ServiceProvider = null;
    }

    public void Dispose()
    {
        AuthorizationBehaviorState.ServiceProvider = null;
    }

    [Fact]
    public async Task ThrowPath_Allow_DispatchesToNext()
    {
        SetupServiceProvider(NewCtx("Admin"));
        var nextCalled = false;
        ValueTask<int> Next(GetThingThrowAllow r, CancellationToken c) { nextCalled = true; return new(r.Id * 2); }

        var result = await AuthorizationBehavior.Handle<GetThingThrowAllow, int>(
            new GetThingThrowAllow(7), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.Equal(14, result);
    }

    [Fact]
    public async Task ThrowPath_Deny_ThrowsAuthorizationDeniedException()
    {
        SetupServiceProvider(NewCtx());
        ValueTask<int> Next(GetThingThrowDeny r, CancellationToken c) => new(r.Id);

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await AuthorizationBehavior.Handle<GetThingThrowDeny, int>(
                new GetThingThrowDeny(7), CancellationToken.None, Next));

        Assert.Equal(AuthorizationFailure.DefaultDenyCode, ex.Failure.Code);
    }

    [Fact]
    public async Task ResultPath_Allow_ReturnsSuccess()
    {
        SetupServiceProvider(NewCtx());
        ValueTask<Result<int, AuthorizationFailure>> Next(GetThingResultAllow r, CancellationToken c)
            => new(Result<int, AuthorizationFailure>.Success(r.Id * 2));

        var result = await AuthorizationBehavior.Handle<GetThingResultAllow, Result<int, AuthorizationFailure>>(
            new GetThingResultAllow(5), CancellationToken.None, Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task ResultPath_Deny_ReturnsFailureWithoutThrowing()
    {
        SetupServiceProvider(NewCtx());
        ValueTask<Result<int, AuthorizationFailure>> Next(GetThingResultDeny r, CancellationToken c)
            => new(Result<int, AuthorizationFailure>.Success(r.Id));

        var result = await AuthorizationBehavior.Handle<GetThingResultDeny, Result<int, AuthorizationFailure>>(
            new GetThingResultDeny(5), CancellationToken.None, Next);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }

    [Fact]
    public async Task MultiPolicyAnd_AllowsWhenAllPoliciesPass()
    {
        SetupServiceProvider(NewCtx("Admin", "Premium"));
        ValueTask<int> Next(GetThingAdminPremium r, CancellationToken c) => new(r.Id);

        var result = await AuthorizationBehavior.Handle<GetThingAdminPremium, int>(
            new GetThingAdminPremium(99), CancellationToken.None, Next);

        Assert.Equal(99, result);
    }

    [Fact]
    public async Task MultiPolicyAnd_DeniesIfAnyPolicyFails()
    {
        // First policy (AdminOnly) passes; second (Premium) denies.
        SetupServiceProvider(NewCtx("Admin"));
        ValueTask<int> Next(GetThingAdminPremium r, CancellationToken c) => new(r.Id);

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await AuthorizationBehavior.Handle<GetThingAdminPremium, int>(
                new GetThingAdminPremium(99), CancellationToken.None, Next));
    }

    [Fact]
    public async Task StackingOrder_FirstDenyShortCircuits()
    {
        // [Authorize("AdminOnly")] [Authorize("Premium")] — AdminOnly evaluated first.
        // Caller has no roles: AdminOnly denies, Premium policy never invoked.
        // Verified indirectly: handler `next` not called.
        SetupServiceProvider(NewCtx());
        var nextCalled = false;
        ValueTask<int> Next(GetThingAdminPremium r, CancellationToken c) { nextCalled = true; return new(r.Id); }

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await AuthorizationBehavior.Handle<GetThingAdminPremium, int>(
                new GetThingAdminPremium(99), CancellationToken.None, Next));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task UnauthorizedRequest_BypassesBehavior_DispatchesToNext()
    {
        SetupServiceProvider(NewCtx());
        var nextCalled = false;
        ValueTask<int> Next(GetThingUnauthorized r, CancellationToken c) { nextCalled = true; return new(r.Id); }

        var result = await AuthorizationBehavior.Handle<GetThingUnauthorized, int>(
            new GetThingUnauthorized(42), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CancellationToken_FlowsIntoPolicy()
    {
        SetupServiceProvider(NewCtx());
        ValueTask<int> Next(GetThingCancellable r, CancellationToken c) => new(r.Id);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await AuthorizationBehavior.Handle<GetThingCancellable, int>(
                new GetThingCancellable(1), cts.Token, Next));
    }

    [Fact]
    public async Task NoServiceProvider_PassesThroughToNext()
    {
        AuthorizationBehaviorState.ServiceProvider = null;
        var nextCalled = false;
        ValueTask<int> Next(GetThingThrowDeny r, CancellationToken c) { nextCalled = true; return new(r.Id); }

        var result = await AuthorizationBehavior.Handle<GetThingThrowDeny, int>(
            new GetThingThrowDeny(7), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.Equal(7, result);
    }

    [Fact]
    public void EagerValidation_FailsWhenPolicyTypeNotInDi_AndAutoRegisterDisabled()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediator()
                    .WithAuthorization(o =>
                    {
                        o.AutoRegisterDiscoveredPolicies = false;
                        o.UseAnonymousSecurityContext();
                    }));

        Assert.Contains("AdminOnlyPolicy", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoRegister_RegistersAllDiscoveredPolicies()
    {
        var services = new ServiceCollection();
        services.AddMediator()
                .WithAuthorization(o => o.UseAnonymousSecurityContext());

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<AdminOnlyPolicy>());
        Assert.NotNull(sp.GetService<PremiumPolicy>());
        Assert.NotNull(sp.GetService<AlwaysDenyPolicy>());
    }

    [Fact]
    public void AutoRegister_OptOut_PolicyManuallyRegistered_PassesValidation()
    {
        var services = new ServiceCollection();
        services.AddScoped<AdminOnlyPolicy>();
        services.AddScoped<PremiumPolicy>();
        services.AddScoped<AlwaysAllowPolicy>();
        services.AddScoped<AlwaysDenyPolicy>();
        services.AddScoped<CancellablePolicy>();

        services.AddMediator()
                .WithAuthorization(o =>
                {
                    o.AutoRegisterDiscoveredPolicies = false;
                    o.UseAnonymousSecurityContext();
                });

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<AdminOnlyPolicy>());
    }

    [Fact]
    public void EagerValidation_MissingSecurityContextSource_Throws()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediator().WithAuthorization());
        Assert.Contains("UseAnonymousSecurityContext", ex.Message, StringComparison.Ordinal);
    }

    private static void SetupServiceProvider(ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddScoped<AdminOnlyPolicy>();
        services.AddScoped<PremiumPolicy>();
        services.AddScoped<AlwaysAllowPolicy>();
        services.AddScoped<AlwaysDenyPolicy>();
        services.AddScoped<CancellablePolicy>();
        services.AddScoped<ISecurityContext>(_ => ctx);
        AuthorizationBehaviorState.ServiceProvider = services.BuildServiceProvider();
    }

    private static ISecurityContext NewCtx(params string[] roles) =>
        new TestCtx("user-1", new HashSet<string>(roles, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

    private sealed record TestCtx(string Id,
                                  IReadOnlySet<string> Roles,
                                  IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
}
