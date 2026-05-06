namespace ZeroAlloc.Mediator.AotSmoke.Internal;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        // Warmup — JIT-compile, populate type-handle caches, allocate one-time fixtures.
        action();
        action();

        // Flush warmup garbage so it can't leak into the measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        var totalBudget = (long)budgetBytes * iterations;
        if (allocated > totalBudget)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {allocated} B total over {iterations} iterations " +
                $"(~{perCall} B/call avg), budget is {budgetBytes} B/call ({totalBudget} B total). " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }

    public static void AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        static T Drain(ValueTask<T> t)
        {
            if (!t.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "AllocationGate: sync-completion-required — the supplied ValueTask did not " +
                    "complete synchronously. Awaiter machinery would pollute the measurement; " +
                    "the API under test must return an already-completed ValueTask.");
            }
            return t.Result;
        }

        // Warmup.
        Drain(action());
        Drain(action());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) Drain(action());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        var totalBudget = (long)budgetBytes * iterations;
        if (allocated > totalBudget)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {allocated} B total over {iterations} iterations " +
                $"(~{perCall} B/call avg), budget is {budgetBytes} B/call ({totalBudget} B total). " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }
}
