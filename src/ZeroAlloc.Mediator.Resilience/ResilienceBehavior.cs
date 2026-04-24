using ZeroAlloc.Mediator;
using ZeroAlloc.Resilience;

namespace ZeroAlloc.Mediator.Resilience;

[PipelineBehavior]
public sealed class ResilienceBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var retry    = ResilienceAttributeCache<TRequest>.Retry;
        var cb       = ResilienceAttributeCache<TRequest>.CircuitBreaker;
        var timeout  = ResilienceAttributeCache<TRequest>.Timeout;

        // Fast path: no policies on this request type.
        if (retry is null && cb is null && timeout is null)
            return await next(request, ct).ConfigureAwait(false);

        // Circuit breaker fast-reject (no attempt even started).
        if (cb is not null && !cb.CanExecute())
            throw new ResilienceException(
                ResiliencePolicy.CircuitBreaker,
                $"Circuit breaker for {typeof(TRequest).Name} is open.");

        // Total-operation timeout CTS (wraps all retries + backoff).
        CancellationTokenSource? totalCts = null;
        if (timeout is not null)
        {
            totalCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : new CancellationTokenSource();
            totalCts.CancelAfter(timeout.TotalMs);
        }

        try
        {
            var effectiveCt = totalCts?.Token ?? ct;

            if (retry is not null)
                return await ExecuteWithRetry<TRequest, TResponse>(request, next, retry, cb, effectiveCt)
                    .ConfigureAwait(false);

            // No retry — single attempt with optional circuit breaker.
            return await ExecuteSingle<TRequest, TResponse>(request, next, cb, effectiveCt)
                .ConfigureAwait(false);
        }
        finally
        {
            totalCts?.Dispose();
        }
    }

    // ── Single-attempt (no retry) ─────────────────────────────────────────────

    private static async ValueTask<TResponse> ExecuteSingle<TRequest, TResponse>(
        TRequest request,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next,
        CircuitBreakerPolicy? cb,
        CancellationToken ct)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            var result = await next(request, ct).ConfigureAwait(false);
            cb?.OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            cb?.OnFailure(ex);
            throw;
        }
    }

    // ── Retry loop ────────────────────────────────────────────────────────────

    private static async ValueTask<TResponse> ExecuteWithRetry<TRequest, TResponse>(
        TRequest request,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next,
        RetryPolicy retry,
        CircuitBreakerPolicy? cb,
        CancellationToken totalCt)
        where TRequest : IRequest<TResponse>
    {
        Exception? lastEx = null;

        for (int attempt = 0; attempt < retry.MaxAttempts; attempt++)
        {
            // Per-attempt timeout: link against the total-timeout token.
            CancellationTokenSource? attemptCts = null;
            CancellationToken attemptCt;
            if (retry.PerAttemptTimeoutMs > 0)
            {
                attemptCts = totalCt.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(totalCt)
                    : new CancellationTokenSource();
                attemptCts.CancelAfter(retry.PerAttemptTimeoutMs);
                attemptCt = attemptCts.Token;
            }
            else
            {
                attemptCt = totalCt;
            }

            try
            {
                var result = await next(request, attemptCt).ConfigureAwait(false);
                cb?.OnSuccess();
                return result;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                cb?.OnFailure(ex);

                // per-attempt timeout fires OperationCanceledException; treat as transient failure and retry if attempts remain.
                // Don't retry if the total-timeout elapsed.
                if (totalCt.IsCancellationRequested) break;
                // Don't delay after the last attempt.
                if (attempt == retry.MaxAttempts - 1) break;

                var backoffMs = retry.GetBackoffMs(attempt);
                await Task.Delay(backoffMs, totalCt).ConfigureAwait(false);
            }
            finally
            {
                attemptCts?.Dispose();
            }
        }

        throw new ResilienceException(
            ResiliencePolicy.Retry,
            $"All {retry.MaxAttempts} attempt(s) failed for {typeof(TRequest).Name}.",
            lastEx);
    }
}
