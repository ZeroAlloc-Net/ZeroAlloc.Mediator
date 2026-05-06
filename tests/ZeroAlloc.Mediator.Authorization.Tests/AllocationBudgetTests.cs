using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.AotSmoke.Internal;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Lifted from ZeroAlloc.Authorization (PR #11). Validates the AllocationGate helper itself
// (3 self-tests) plus four budget tests covering the documented zero-allocation hot paths
// of an AuthorizationBehavior.Handle invocation: throw allow, throw deny via anonymous,
// Result allow, IsAuthorizedAsync allow.
[Collection("non-parallel-authorization")]
public sealed class AllocationBudgetTests
{
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

    [Fact]
    public void Behavior_ThrowAllow_ZeroAllocation()
    {
        SetupSp(NewCtx("Admin"));
        var req = new GetThingThrowAllow(7);

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => AuthorizationBehavior.Handle<GetThingThrowAllow, int>(req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)),
            "AuthorizationBehavior.Handle (throw allow)");
    }

    [Fact]
    public void Behavior_ThrowDeny_AnonymousContext_AllocationBudget()
    {
        SetupSp(NewCtx());
        var req = new GetThingThrowDeny(7);

        // Deny path throws an exception per call — the exception object itself is an
        // unavoidable allocation. Budget reflects an Exception + stack trace + interpolated
        // Message string; observed ~1256 B/call on net10. Set generously (2 KB) to avoid
        // flakiness across runtimes; the gate still catches order-of-magnitude regressions.
        AllocationGate.AssertBudget(2048, 100, () =>
        {
            try
            {
                _ = AuthorizationBehavior.Handle<GetThingThrowDeny, int>(req, CancellationToken.None,
                    static (r, _) => ValueTask.FromResult(r.Id))
                    .GetAwaiter().GetResult();
            }
            catch (AuthorizationDeniedException)
            {
                /* expected */
            }
        }, "AuthorizationBehavior.Handle (throw deny anonymous)");
    }

    [Fact]
    public void Behavior_ResultAllow_ZeroAllocation()
    {
        SetupSp(NewCtx("Admin"));
        var req = new GetThingResultAllow(5);

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => AuthorizationBehavior.Handle<GetThingResultAllow, Result<int, AuthorizationFailure>>(
                req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2)),
            "AuthorizationBehavior.Handle (result allow)");
    }

    [Fact]
    public void Policy_IsAuthorizedAsync_AllowPath_ZeroAllocation()
    {
        IAuthorizationPolicy policy = new AdminOnlyPolicy();
        var ctx = NewCtx("Admin");

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => policy.IsAuthorizedAsync(ctx),
            "Policy.IsAuthorizedAsync (allow)");
    }

    private static void SetupSp(ISecurityContext ctx)
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
