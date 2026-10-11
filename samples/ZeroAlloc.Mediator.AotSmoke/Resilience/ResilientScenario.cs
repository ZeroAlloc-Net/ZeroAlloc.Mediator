using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Resilience;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Mediator.AotSmoke.Resilience;

#pragma warning disable MA0048
[MediatorRetry(MaxAttempts = 3, BackoffMs = 1)]
public readonly record struct AotRetried(int Id) : IRequest<int>;

[MediatorRetry(MaxAttempts = 2, BackoffMs = 1, PerAttemptTimeoutMs = 50)]
public readonly record struct AotAttemptTimedOut(int Id) : IRequest<int>;

[MediatorTimeout(Ms = 50)]
public readonly record struct AotTimedOut(int Id) : IRequest<int>;

[MediatorCircuitBreaker(MaxFailures = 1, ResetMs = 60_000)]
public readonly record struct AotBroken(int Id) : IRequest<int>;

// Fails twice, then answers: the retry policy has to absorb both failures.
public sealed class AotRetriedHandler : IRequestHandler<AotRetried, int>
{
    internal static int Calls;

    public ValueTask<int> Handle(AotRetried r, CancellationToken ct)
        => Interlocked.Increment(ref Calls) < 3
            ? ValueTask.FromException<int>(new InvalidOperationException("transient"))
            : ValueTask.FromResult(r.Id);
}

// The first attempt hangs until its per-attempt timeout cancels it; the second answers.
public sealed class AotAttemptTimedOutHandler : IRequestHandler<AotAttemptTimedOut, int>
{
    internal static int Calls;

    public async ValueTask<int> Handle(AotAttemptTimedOut r, CancellationToken ct)
    {
        if (Interlocked.Increment(ref Calls) == 1)
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return r.Id;
    }
}

// Never answers: only the total timeout ends it.
public sealed class AotTimedOutHandler : IRequestHandler<AotTimedOut, int>
{
    public async ValueTask<int> Handle(AotTimedOut r, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return r.Id;
    }
}

public sealed class AotBrokenHandler : IRequestHandler<AotBroken, int>
{
    internal static int Calls;

    public ValueTask<int> Handle(AotBroken r, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        return ValueTask.FromException<int>(new InvalidOperationException("down"));
    }
}
#pragma warning restore MA0048

// Sends through IMediator, so the generated pipeline has to run ResilienceBehavior, and reads
// the policies from the request attributes under NativeAOT.
public static class ResilientScenario
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddMediator().WithResilience();
        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var retried = await mediator.Send(new AotRetried(7), CancellationToken.None).ConfigureAwait(false);
        if (retried != 7 || Volatile.Read(ref AotRetriedHandler.Calls) != 3)
            throw new InvalidOperationException(
                $"Retry: expected 7 after 3 calls, got {retried} after {AotRetriedHandler.Calls}");
        Console.WriteLine("Mediator.Resilience: retry OK");

        var attempted = await mediator.Send(new AotAttemptTimedOut(8), CancellationToken.None).ConfigureAwait(false);
        if (attempted != 8 || Volatile.Read(ref AotAttemptTimedOutHandler.Calls) != 2)
            throw new InvalidOperationException(
                $"Per-attempt timeout: expected 8 after 2 calls, got {attempted} after {AotAttemptTimedOutHandler.Calls}");
        Console.WriteLine("Mediator.Resilience: per-attempt timeout OK");

        try
        {
            await mediator.Send(new AotTimedOut(9), CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Timeout: the send completed instead of timing out");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Mediator.Resilience: timeout OK");
        }

        try
        {
            await mediator.Send(new AotBroken(1), CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Circuit breaker: the failing handler did not throw");
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "down", StringComparison.Ordinal))
        {
            // The one failure the breaker allows; it opens the circuit.
        }

        try
        {
            await mediator.Send(new AotBroken(2), CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("Circuit breaker: the open circuit let a send through");
        }
        catch (ResilienceException ex) when (ex.Policy == ResiliencePolicy.CircuitBreaker)
        {
            // The open circuit rejected the send without running the handler.
        }

        if (Volatile.Read(ref AotBrokenHandler.Calls) != 1)
            throw new InvalidOperationException(
                $"Circuit breaker: expected the handler to run once, ran {AotBrokenHandler.Calls} times");
        Console.WriteLine("Mediator.Resilience: circuit breaker OK");
    }
}
