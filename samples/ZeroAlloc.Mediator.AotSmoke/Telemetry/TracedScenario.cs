using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Mediator.AotSmoke.Telemetry;

#pragma warning disable MA0048
public readonly record struct FailingPing(string Message) : IRequest<string>;
#pragma warning restore MA0048

public sealed class FailingPingHandler : IRequestHandler<FailingPing, string>
{
    public ValueTask<string> Handle(FailingPing request, CancellationToken ct)
        => throw new InvalidOperationException($"FailingPing: {request.Message}");
}

// With ZeroAlloc.Mediator.Telemetry referenced, TelemetryBehavior runs inside the generated Send.
// One Send must still produce exactly one mediator.send span, and the behavior must record every
// Send on both instruments: a success with no tags, a failure tagged error.type with the
// exception's full type name, which must survive NativeAOT.
public static class TracedScenario
{
    public static async Task RunAsync()
    {
        var spans = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = src => string.Equals(src.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        var counts = new List<string>();
        var durations = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal))
                    l.EnableMeasurementEvents(instrument);
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (string.Equals(instrument.Name, "mediator.requests_total", StringComparison.Ordinal))
                counts.Add($"{value} {Describe(tags)}");
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (string.Equals(instrument.Name, "mediator.request_duration_ms", StringComparison.Ordinal))
                durations.Add(Describe(tags));
        });
        meterListener.Start();

        await AssertSucceedingSendAsync(spans).ConfigureAwait(false);
        await AssertFailingSendAsync(spans).ConfigureAwait(false);
        AssertMetrics(counts, durations);
    }

    private static async Task AssertSucceedingSendAsync(List<Activity> spans)
    {
        var pong = await Mediator.Send(new Ping("traced"), CancellationToken.None).ConfigureAwait(false);
        if (!string.Equals(pong, "Pong: traced", StringComparison.Ordinal))
            throw new InvalidOperationException($"Telemetry: Send returned '{pong}'");

        var sendSpans = spans.FindAll(a => string.Equals(a.OperationName, "mediator.send", StringComparison.Ordinal));
        if (sendSpans.Count != 1)
            throw new InvalidOperationException($"Telemetry: expected 1 mediator.send span, got {sendSpans.Count}");
        if (sendSpans[0].Parent is not null)
            throw new InvalidOperationException("Telemetry: mediator.send span has a parent");
        if (!string.Equals(sendSpans[0].GetTagItem("request.type") as string, "Ping", StringComparison.Ordinal))
            throw new InvalidOperationException("Telemetry: mediator.send span is missing request.type=Ping");
    }

    private static async Task AssertFailingSendAsync(List<Activity> spans)
    {
        string? failure = null;
        try
        {
            await Mediator.Send(new FailingPing("boom"), CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            failure = ex.Message;
        }
        if (!string.Equals(failure, "FailingPing: boom", StringComparison.Ordinal))
            throw new InvalidOperationException($"Telemetry: FailingPing expected to throw, got '{failure}'");

        var failedSpans = spans.FindAll(a => string.Equals(a.GetTagItem("request.type") as string, "FailingPing", StringComparison.Ordinal));
        if (failedSpans.Count != 1 || failedSpans[0].Status != ActivityStatusCode.Error)
            throw new InvalidOperationException("Telemetry: expected 1 mediator.send span for FailingPing marked Error");
        if (!string.Equals(failedSpans[0].GetTagItem("error.type") as string, "System.InvalidOperationException", StringComparison.Ordinal))
            throw new InvalidOperationException("Telemetry: FailingPing span is missing error.type=System.InvalidOperationException");
    }

    private static void AssertMetrics(List<string> counts, List<string> durations)
    {
        const string expectedCounts = "1 [] | 1 [error.type=System.InvalidOperationException]";
        var actualCounts = string.Join(" | ", counts);
        if (!string.Equals(actualCounts, expectedCounts, StringComparison.Ordinal))
            throw new InvalidOperationException($"Telemetry: expected mediator.requests_total '{expectedCounts}', got '{actualCounts}'");

        const string expectedDurations = "[] | [error.type=System.InvalidOperationException]";
        var actualDurations = string.Join(" | ", durations);
        if (!string.Equals(actualDurations, expectedDurations, StringComparison.Ordinal))
            throw new InvalidOperationException($"Telemetry: expected mediator.request_duration_ms tags '{expectedDurations}', got '{actualDurations}'");
    }

    private static string Describe(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var parts = new List<string>(tags.Length);
        foreach (ref readonly var tag in tags)
            parts.Add($"{tag.Key}={tag.Value}");
        return $"[{string.Join(",", parts)}]";
    }
}
