using ZeroAlloc.Resilience;

namespace ZeroAlloc.Mediator.Resilience;

internal static class ResilienceAttributeCache<TRequest>
{
    internal static readonly RetryPolicy? Retry;
    internal static readonly CircuitBreakerPolicy? CircuitBreaker;
    internal static readonly TimeoutPolicy? Timeout;

    static ResilienceAttributeCache()
    {
        var type = typeof(TRequest);

        var retry = (MediatorRetryAttribute?)Attribute.GetCustomAttribute(type, typeof(MediatorRetryAttribute));
        if (retry is not null)
            Retry = new RetryPolicy(retry.MaxAttempts, retry.BackoffMs, retry.Jitter, retry.PerAttemptTimeoutMs);

        var cb = (MediatorCircuitBreakerAttribute?)Attribute.GetCustomAttribute(type, typeof(MediatorCircuitBreakerAttribute));
        if (cb is not null)
            CircuitBreaker = new CircuitBreakerPolicy(cb.MaxFailures, cb.ResetMs, cb.HalfOpenProbes);

        var timeout = (MediatorTimeoutAttribute?)Attribute.GetCustomAttribute(type, typeof(MediatorTimeoutAttribute));
        if (timeout is not null)
            Timeout = new TimeoutPolicy(timeout.Ms);
    }
}
