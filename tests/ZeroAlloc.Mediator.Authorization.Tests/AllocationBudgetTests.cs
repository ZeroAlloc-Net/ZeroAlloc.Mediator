using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator.AotSmoke.Internal;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Allocation budgets for the documented hot paths of AuthorizationBehavior in v2. Each
// budgeted test wires the DI container the production way (services.AddZeroAllocAuthorization()
// + services.AddMediator().WithAuthorization(...)) and drives AuthorizationBehavior.Handle
// directly — the cross-assembly behavior is not auto-wired into MediatorService by the
// Mediator generator, so a direct call is required to measure the v2 path. Allocation profile
// is identical either way (the dispatcher would inline a static lambda chain around the same
// Handle call).
//
// Throw-path deny intentionally is NOT budgeted: throwing an exception allocates the
// Exception object + stack trace, which is unavoidable and dwarfs any pipeline overhead.
[Collection("non-parallel-authorization")]
public sealed class AllocationBudgetTests
{
    // ---- AllocationGate self-tests ----

    [Fact]
    public void Gate_DetectsAllocation_WhenActionAllocates()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 1000,
                action: () => _ = new object(),
                label: "test-allocator"));

        Assert.Contains("test-allocator", ex.Message, StringComparison.Ordinal);
        Assert.Contains("budget is 0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_RejectsValueTask_NotCompletedSynchronously()
    {
        var pending = new TaskCompletionSource<int>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudgetValueTask<int>(
                budgetBytes: 0,
                iterations: 1,
                action: () => new ValueTask<int>(pending.Task),
                label: "pending"));

        Assert.Contains("sync-completion-required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_TolerantOfWarmupOnlyAllocations()
    {
        var firstCall = true;
        AllocationGate.AssertBudget(0, 1000, () =>
        {
            if (firstCall) { firstCall = false; _ = new object(); }
        }, "warmup-only-allocator");
    }

    // ---- v2 behavior allocation budgets ----

    [Fact]
    public void Allow_HappyPath_ZeroAlloc()
    {
        // [RequirePolicy("AlwaysAllow")] GetThingThrowAllow + policy returning Success. End-to-end
        // happy-path through AuthorizationBehavior.Handle. Budget rationale:
        //   Release / AOT:  0 B/call (async state machine elided by the JIT optimizer).
        //   Debug path:     ~248 B/call (Debug-mode async state machine box). 512 B absorbs the
        //                   Debug box without masking real regressions.
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        var req = new GetThingThrowAllow(7);

        AllocationGate.AssertBudgetValueTask(512, 1000,
            () => AuthorizationBehavior.Handle<GetThingThrowAllow, int>(req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)),
            "AuthorizationBehavior.Handle (allow happy path)");
    }

    [Fact]
    public void IAuthorizedRequest_DenyPath_ZeroAlloc()
    {
        // IAuthorizedRequest deny path returns Result<T, AuthorizationFailure>.Failure(...) via
        // the AuthorizationFailureFactory<TResponse> shape detector — no exception, no throw,
        // no allocation. Budget rationale identical to Allow_HappyPath_ZeroAlloc.
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        var req = new GetThingResultDeny(1);

        AllocationGate.AssertBudgetValueTask(512, 1000,
            () => AuthorizationBehavior.Handle<GetThingResultDeny, Result<int, AuthorizationFailure>>(
                req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id)),
            "AuthorizationBehavior.Handle (IAuthorizedRequest deny path)");
    }

    [Fact]
    public void Policy_EvaluateAsync_AllowPath_ZeroAllocation()
    {
        // Direct policy evaluation — no behavior, no DI. Confirms v2's single-method
        // IAuthorizationPolicy.EvaluateAsync returning UnitResult<AuthorizationFailure> is
        // zero-allocation on the sync-completing happy path. Tightest budget (0 B).
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = TestSecurityContexts.With("Admin");

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => policy.EvaluateAsync(ctx),
            "Policy.EvaluateAsync (allow)");
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
