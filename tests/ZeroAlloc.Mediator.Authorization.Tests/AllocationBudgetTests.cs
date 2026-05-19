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

// Placeholder — rewritten in Task 8 of the Mediator.Authorization v2.0.0 plan. Kept minimal
// here so the test project still compiles after Task 5 lands. The AllocationGate self-tests
// remain valuable as smoke checks for the helper itself; behaviour-allocation budgets land
// in the Task 8 rewrite.
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
}
