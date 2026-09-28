using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Telemetry;

/// <summary>
/// Pipeline behavior that records per-request metrics for every <see cref="IRequest{TResponse}"/>
/// dispatch: the <c>mediator.requests_total</c> counter and the <c>mediator.request_duration_ms</c>
/// histogram, both on the <c>ZeroAlloc.Mediator</c> meter. Both record every request, whether it
/// succeeds or throws. Outermost in the pipeline
/// (Order = -3000) so the duration covers authorization denials, validation errors, cache hits
/// and retries.
/// </summary>
/// <remarks>
/// <para>
/// This behavior does not open a trace span. The generated <c>Send</c> already opens one
/// <c>mediator.send</c> activity on the <c>ZeroAlloc.Mediator</c> activity source, with or without
/// this package, tagged <c>request.type</c> with the request's simple type name and marked as an
/// error, tagged with the same <c>error.type</c> value as the metrics below, when the request
/// throws. This behavior runs inside that span.
/// </para>
/// <para>
/// A request that throws is recorded on both instruments with an <c>error.type</c> tag set to the
/// exception's fully-qualified type name, such as <c>System.InvalidOperationException</c>, following
/// the OpenTelemetry convention for recording errors. A request that completes is recorded with no
/// tags, so the error rate is the <c>error.type</c>-tagged count over the total count. A cancelled
/// request is a failure, tagged with its <see cref="OperationCanceledException"/> type.
/// </para>
/// <para>
/// The metrics carry no request-type dimension.
/// </para>
/// <para>
/// Notifications dispatched via <c>Mediator.Publish(...)</c> and streams created via
/// <c>Mediator.CreateStream(...)</c> do not run pipeline behaviors, so they get the generated
/// <c>mediator.publish</c> and <c>mediator.stream</c> spans but no metrics from this behavior.
/// </para>
/// </remarks>
[PipelineBehavior(Order = -3000)]
public sealed class TelemetryBehavior : IPipelineBehavior
{
    private static readonly Meter _meter = new("ZeroAlloc.Mediator");
    private static readonly Counter<long> _requestsTotal = _meter.CreateCounter<long>("mediator.requests_total");
    private static readonly Histogram<double> _requestDurationMs = _meter.CreateHistogram<double>("mediator.request_duration_ms");

    private const string ErrorTypeTag = "error.type";

    // OTel's fallback value for error.type when no more specific value is available.
    private const string OtherErrorType = "_OTHER";

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
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
            // OTel's error.type convention: present only on failure, set to the exception's
            // fully-qualified type name. A success keeps the tag-free series it always had.
            var errorType = new KeyValuePair<string, object?>(ErrorTypeTag, ex.GetType().FullName ?? OtherErrorType);
            _requestsTotal.Add(1, errorType);
            _requestDurationMs.Record(Stopwatch.GetElapsedTime(sw).TotalMilliseconds, errorType);
            throw;
        }
    }
}
