using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Resilience;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Mediator.Resilience.Tests;

// ── Request fixtures ──────────────────────────────────────────────────────────

public readonly record struct PlainRequest(int Value) : IRequest<int>;

[MediatorRetry(MaxAttempts = 3, BackoffMs = 1)]
public readonly record struct RetryRequest(int Value) : IRequest<int>;

[MediatorRetry(MaxAttempts = 2, BackoffMs = 1)]
[MediatorTimeout(Ms = 5_000)]
public readonly record struct RetryWithTimeoutRequest(int Value) : IRequest<int>;

[MediatorRetry(MaxAttempts = 2, BackoffMs = 1, PerAttemptTimeoutMs = 2_000)]
public readonly record struct PerAttemptTimeoutRequest(int Value) : IRequest<int>;

[MediatorCircuitBreaker(MaxFailures = 2, ResetMs = 60_000, HalfOpenProbes = 1)]
public readonly record struct CbRequest(int Value) : IRequest<int>;

// Dedicated type for the circuit-open trip test — isolates its circuit state from CbRequest.
[MediatorCircuitBreaker(MaxFailures = 2, ResetMs = 60_000)]
public readonly record struct CbTripRequest(int Value) : IRequest<int>;

[MediatorTimeout(Ms = 5_000)]
public readonly record struct TimeoutRequest(int Value) : IRequest<int>;

[MediatorTimeout(Ms = 50)]
public readonly record struct ShortTimeoutRequest(int Value) : IRequest<int>;

// ── Tests ─────────────────────────────────────────────────────────────────────

public sealed class ResilienceBehaviorTests
{
    // ── Passthrough ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PlainRequest_PassesThroughWithNoOverhead()
    {
        var callCount = 0;
        ValueTask<int> Next(PlainRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value * 2);
        }

        var result = await ResilienceBehavior.Handle(new PlainRequest(5), CancellationToken.None, Next);

        Assert.Equal(10, result);
        Assert.Equal(1, callCount);
    }

    // ── Retry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_RetryRequest_ReturnsOnFirstSuccess()
    {
        var callCount = 0;
        ValueTask<int> Next(RetryRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value);
        }

        var result = await ResilienceBehavior.Handle(new RetryRequest(42), CancellationToken.None, Next);

        Assert.Equal(42, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Handle_RetryRequest_RetriesOnFailureAndSucceeds()
    {
        var callCount = 0;
        ValueTask<int> Next(RetryRequest r, CancellationToken c)
        {
            callCount++;
            if (callCount < 3)
                throw new InvalidOperationException("transient");
            return ValueTask.FromResult(99);
        }

        var result = await ResilienceBehavior.Handle(new RetryRequest(0), CancellationToken.None, Next);

        Assert.Equal(99, result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task Handle_RetryRequest_ThrowsAfterAllAttemptsExhausted()
    {
        var callCount = 0;
        ValueTask<int> Next(RetryRequest r, CancellationToken c)
        {
            callCount++;
            throw new InvalidOperationException("always fails");
        }

        var ex = await Assert.ThrowsAsync<ResilienceException>(
            () => ResilienceBehavior.Handle(new RetryRequest(0), CancellationToken.None, Next).AsTask());

        Assert.Equal(ResiliencePolicy.Retry, ex.Policy);
        Assert.Equal(3, callCount); // MaxAttempts = 3
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TimeoutRequest_SucceedsWhenWithinTimeout()
    {
        var callCount = 0;
        ValueTask<int> Next(TimeoutRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value);
        }

        var result = await ResilienceBehavior.Handle(new TimeoutRequest(7), CancellationToken.None, Next);

        Assert.Equal(7, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Handle_TimeoutRequest_CancelsWhenTimeoutElapses()
    {
        // ShortTimeoutRequest carries [MediatorTimeout(Ms = 50)].
        // The next delegate awaits forever, so the behavior's totalCts.CancelAfter(50) fires first.
        static async ValueTask<int> Next(ShortTimeoutRequest r, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return 0;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ResilienceBehavior.Handle(new ShortTimeoutRequest(0), CancellationToken.None, Next).AsTask());
    }

    // ── Circuit Breaker ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CbRequest_SucceedsWhenCircuitClosed()
    {
        var callCount = 0;
        ValueTask<int> Next(CbRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value);
        }

        var result = await ResilienceBehavior.Handle(new CbRequest(3), CancellationToken.None, Next);

        Assert.Equal(3, result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Handle_CbRequest_ThrowsWhenCircuitOpen()
    {
        // Trip the circuit by injecting failures directly into the cached policy.
        // Uses CbTripRequest (dedicated type) so its circuit state is fully isolated.
        var cb = ResilienceAttributeCache<CbTripRequest>.CircuitBreaker!;

        // Record enough failures to trip the circuit (MaxFailures = 2).
        cb.OnFailure(new InvalidOperationException("f1"));
        cb.OnFailure(new InvalidOperationException("f2"));

        var callCount = 0;
        ValueTask<int> Next(CbTripRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(0);
        }

        // Circuit should now be open — behavior throws immediately.
        var ex = await Assert.ThrowsAsync<ResilienceException>(
            () => ResilienceBehavior.Handle(new CbTripRequest(0), CancellationToken.None, Next).AsTask());

        Assert.Equal(ResiliencePolicy.CircuitBreaker, ex.Policy);
        Assert.Equal(0, callCount); // next was never called
    }

    // ── DI extension ─────────────────────────────────────────────────────────

    [Fact]
    public void AddMediatorResilience_RegistersMarker()
    {
        var services = new ServiceCollection();
        services.AddMediatorResilience();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<MediatorResilienceMarker>());
    }

    [Fact]
    public void AddMediatorResilience_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddMediatorResilience();
        services.AddMediatorResilience(); // second call must not throw or double-register

        var registrations = services.Count(d => d.ServiceType == typeof(MediatorResilienceMarker));
        Assert.Equal(1, registrations);
    }

    // ── Attribute cache ───────────────────────────────────────────────────────

    [Fact]
    public void ResilienceAttributeCache_PlainRequest_HasNoPolicies()
    {
        Assert.Null(ResilienceAttributeCache<PlainRequest>.Retry);
        Assert.Null(ResilienceAttributeCache<PlainRequest>.CircuitBreaker);
        Assert.Null(ResilienceAttributeCache<PlainRequest>.Timeout);
    }

    [Fact]
    public void ResilienceAttributeCache_RetryRequest_HasRetryPolicy()
    {
        var retry = ResilienceAttributeCache<RetryRequest>.Retry;
        Assert.NotNull(retry);
        Assert.Equal(3, retry!.MaxAttempts);
        Assert.Equal(1, retry.BackoffMs);
    }

    [Fact]
    public void ResilienceAttributeCache_CbRequest_HasCircuitBreakerPolicy()
    {
        var cb = ResilienceAttributeCache<CbRequest>.CircuitBreaker;
        Assert.NotNull(cb);
        Assert.Equal(CircuitBreakerState.Closed, cb!.State);
    }

    [Fact]
    public void ResilienceAttributeCache_TimeoutRequest_HasTimeoutPolicy()
    {
        var timeout = ResilienceAttributeCache<TimeoutRequest>.Timeout;
        Assert.NotNull(timeout);
        Assert.Equal(5_000, timeout!.TotalMs);
    }
}
