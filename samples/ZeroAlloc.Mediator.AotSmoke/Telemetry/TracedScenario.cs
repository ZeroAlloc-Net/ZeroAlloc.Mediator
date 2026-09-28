using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Mediator.AotSmoke.Telemetry;

// With ZeroAlloc.Mediator.Telemetry referenced, TelemetryBehavior runs inside the generated Send.
// One Send must still produce exactly one mediator.send span, and the behavior's counter must fire.
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

        long requests = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "mediator.requests_total", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref requests, value));
        meterListener.Start();

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
        if (Interlocked.Read(ref requests) != 1)
            throw new InvalidOperationException($"Telemetry: expected mediator.requests_total 1, got {requests}");
    }
}
