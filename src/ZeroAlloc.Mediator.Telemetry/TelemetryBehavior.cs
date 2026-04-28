using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Telemetry;

/// <summary>
/// Pipeline behavior that wraps every <see cref="IRequest{TResponse}"/> dispatch
/// with an OpenTelemetry <see cref="Activity"/> and records per-request counters and
/// duration histograms. Outermost in the pipeline (Order = 0) so spans capture
/// retries, cache misses, and validation errors.
/// </summary>
/// <remarks>
/// Notifications dispatched via <c>Mediator.Publish(...)</c> are NOT instrumented in v1 —
/// the Mediator generator's Publish path bypasses pipeline behaviors. A future generator
/// change to run pipeline behaviors on Publish would automatically extend coverage.
/// </remarks>
[PipelineBehavior(Order = 0)]
public sealed class TelemetryBehavior : IPipelineBehavior
{
    private static readonly ActivitySource _activitySource = new("ZeroAlloc.Mediator");
    private static readonly Meter _meter = new("ZeroAlloc.Mediator");
    private static readonly Counter<long> _requestsTotal = _meter.CreateCounter<long>("mediator.requests_total");
    private static readonly Histogram<double> _requestDurationMs = _meter.CreateHistogram<double>("mediator.request_duration_ms");

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        using var activity = _activitySource.StartActivity("mediator.send");
        activity?.SetTag("mediator.request_type", typeof(TRequest).FullName);
        var sw = Stopwatch.GetTimestamp();

        try
        {
            var result = await next(request, ct).ConfigureAwait(false);
            _requestsTotal.Add(1);
            _requestDurationMs.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _requestDurationMs.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
            throw;
        }
    }
}
