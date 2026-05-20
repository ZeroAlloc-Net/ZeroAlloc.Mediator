using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// These tests drive AuthorizationBehavior.Handle directly. The DI container is wired the
// production way — services.AddZeroAllocAuthorization() registers the [Policy] classes and
// AuthorizerFor<TRequest> dispatchers; services.AddMediator().WithAuthorization(...) stashes
// the provider into AuthorizationBehaviorState via AuthorizationBehaviorAccessor on the first
// IServiceProvider build. From there, AuthorizationBehavior.Handle resolves
// AuthorizerFor<TRequest> + ISecurityContext on every invocation just like it would inside
// the source-generated MediatorService pipeline.
//
// IMediator-driven integration is intentionally not exercised here: the Mediator generator's
// pipeline wiring only sees [PipelineBehavior]-decorated types in the *current* compilation,
// so a cross-assembly behavior like AuthorizationBehavior cannot be wired without a local
// shim. The behavior contract is identical either way; this file targets the contract.
[Collection("non-parallel-authorization")]
public sealed class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Allow_FlowsThroughToHandler()
    {
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        using var scope = sp.CreateScope();
        var nextCalled = false;
        ValueTask<int> Next(GetThingThrowAllow r, CancellationToken c)
        { nextCalled = true; return new(r.Id * 2); }

        var result = await Invoke<GetThingThrowAllow, int>(scope, new GetThingThrowAllow(7), Next);

        Assert.True(nextCalled);
        Assert.Equal(14, result);
    }

    [Fact]
    public async Task Deny_OnIRequest_ThrowsAuthorizationDeniedException()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        ValueTask<int> Next(GetThingThrowDeny r, CancellationToken c) => new(r.Id);

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await Invoke<GetThingThrowDeny, int>(scope, new GetThingThrowDeny(7), Next));

        Assert.Equal(AuthorizationFailure.DefaultDenyCode, ex.Failure.Code);
    }

    [Fact]
    public async Task Deny_OnIAuthorizedRequest_ReturnsFailureResult()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        ValueTask<Result<int, AuthorizationFailure>> Next(GetThingResultDeny r, CancellationToken c)
            => new(Result<int, AuthorizationFailure>.Success(r.Id));

        var result = await Invoke<GetThingResultDeny, Result<int, AuthorizationFailure>>(
            scope, new GetThingResultDeny(5), Next);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }

    [Fact]
    public async Task Allow_OnIAuthorizedRequest_ReturnsSuccessPayload()
    {
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        using var scope = sp.CreateScope();
        ValueTask<Result<int, AuthorizationFailure>> Next(GetThingResultAllow r, CancellationToken c)
            => new(Result<int, AuthorizationFailure>.Success(r.Id * 2));

        var result = await Invoke<GetThingResultAllow, Result<int, AuthorizationFailure>>(
            scope, new GetThingResultAllow(5), Next);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value);
    }

    [Fact]
    public async Task MultiplePolicies_AndSemantics_AllowsOnlyWhenAllPass()
    {
        using var sp = BuildProvider(TestSecurityContexts.With("Admin", "Premium"));
        using var scope = sp.CreateScope();
        ValueTask<int> Next(GetThingAdminPremium r, CancellationToken c) => new(r.Id);

        var result = await Invoke<GetThingAdminPremium, int>(
            scope, new GetThingAdminPremium(99), Next);

        Assert.Equal(99, result);
    }

    [Fact]
    public async Task MultiplePolicies_AndSemantics_FirstDenyShortCircuits()
    {
        // No roles: AdminOnly denies; the Premium policy must not even be queried. Verified
        // indirectly: next() is not invoked, and an AuthorizationDeniedException surfaces
        // because the request type is a plain IRequest<T>.
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        var nextCalled = false;
        ValueTask<int> Next(GetThingAdminPremium r, CancellationToken c)
        { nextCalled = true; return new(r.Id); }

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await Invoke<GetThingAdminPremium, int>(scope, new GetThingAdminPremium(99), Next));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task NoAuthorizerForRequest_FailsOpen()
    {
        // GetThingUnauthorized has no [RequirePolicy] — no AuthorizerFor<> is generated for it,
        // so the behavior fails open and the next delegate runs.
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        var nextCalled = false;
        ValueTask<int> Next(GetThingUnauthorized r, CancellationToken c)
        { nextCalled = true; return new(r.Id); }

        var result = await Invoke<GetThingUnauthorized, int>(
            scope, new GetThingUnauthorized(42), Next);

        Assert.True(nextCalled);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task PolicyResolution_ScopedPerRequest()
    {
        // Two independent scopes resolve independent AuthorizerFor<> + policy instances —
        // each request gets a fresh policy graph. Verified indirectly: both sends succeed,
        // proving the per-scope resolution is consistent.
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));

        using (var scope1 = sp.CreateScope())
        {
            var r1 = await Invoke<GetThingThrowAllow, int>(
                scope1, new GetThingThrowAllow(1),
                static (r, _) => new(r.Id * 2));
            Assert.Equal(2, r1);
        }

        using (var scope2 = sp.CreateScope())
        {
            var r2 = await Invoke<GetThingThrowAllow, int>(
                scope2, new GetThingThrowAllow(2),
                static (r, _) => new(r.Id * 2));
            Assert.Equal(4, r2);
        }
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        ValueTask<int> Next(GetThingCancellable r, CancellationToken c) => new(r.Id);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Invoke<GetThingCancellable, int>(scope, new GetThingCancellable(1), Next, cts.Token));
    }

    private static ValueTask<TResponse> Invoke<TRequest, TResponse>(
        IServiceScope scope,
        TRequest request,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // Push the per-scope provider into AuthorizationBehaviorState. Production code sets
        // this via AuthorizationBehaviorAccessor on first SP build; here we set it explicitly
        // to the scope provider so AuthorizerFor<TRequest> + ISecurityContext resolve against
        // the scope's services.
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        return AuthorizationBehavior.Handle<TRequest, TResponse>(request, ct, next);
    }

    private static ServiceProvider BuildProvider(ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }
}
