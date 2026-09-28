using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Telemetry.Tests;

public readonly record struct SpanProbe(int Value) : IRequest<int>;

public sealed class SpanProbeHandler : IRequestHandler<SpanProbe, int>
{
    public ValueTask<int> Handle(SpanProbe request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

public readonly record struct FailingSpanProbe(int Value) : IRequest<int>;

public sealed class FailingSpanProbeHandler : IRequestHandler<FailingSpanProbe, int>
{
    public ValueTask<int> Handle(FailingSpanProbe request, CancellationToken ct)
        => throw new InvalidOperationException("probe failed");
}

// Regression for #236: with ZeroAlloc.Mediator.Telemetry referenced, TelemetryBehavior runs inside the
// generated Send. Only the generated Send may open the mediator.send span; the behavior adds metrics.
[Collection("telemetry-non-parallel")]
public class SendSpanTests
{
    public static TheoryData<string> Paths => new() { "static", "IMediator" };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Send_OpensExactlyOneSpan_AndRecordsMetrics(string path)
    {
        using var metrics = new MetricCapture();
        using var listener = new TestActivityListener("ZeroAlloc.Mediator");

        var result = await SendAsync(path, new SpanProbe(41));

        Assert.Equal(42, result);
        var activity = Assert.Single(listener.StoppedActivities, a => string.Equals(a.OperationName, "mediator.send", StringComparison.Ordinal));
        Assert.Null(activity.Parent);
        Assert.Equal("SpanProbe", activity.GetTagItem("request.type"));
        Assert.Null(activity.GetTagItem("mediator.request_type"));
        Assert.Null(activity.GetTagItem("error.type"));

        // #238: a success is counted without an error.type tag, on both instruments.
        var count = Assert.Single(metrics.RequestsTotal);
        Assert.Equal(1L, count.Value);
        Assert.Null(count.ErrorType);
        var duration = Assert.Single(metrics.DurationsMs);
        Assert.Null(duration.ErrorType);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Send_OnHandlerException_MarksTheSingleSpanAsError(string path)
    {
        using var metrics = new MetricCapture();
        using var listener = new TestActivityListener("ZeroAlloc.Mediator");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(path, new FailingSpanProbe(1)).AsTask());

        Assert.Equal("probe failed", thrown.Message);
        var activity = Assert.Single(listener.StoppedActivities, a => string.Equals(a.OperationName, "mediator.send", StringComparison.Ordinal));
        Assert.Null(activity.Parent);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("probe failed", activity.StatusDescription);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("error.type"));

        // #238: a failure is counted too, tagged error.type with the exception's full type name,
        // and the histogram carries the same tag so the two instruments stay consistent.
        var count = Assert.Single(metrics.RequestsTotal);
        Assert.Equal(1L, count.Value);
        Assert.Equal("System.InvalidOperationException", count.ErrorType);
        var duration = Assert.Single(metrics.DurationsMs);
        Assert.Equal("System.InvalidOperationException", duration.ErrorType);
    }

    private static async ValueTask<int> SendAsync(string path, SpanProbe request)
    {
        if (string.Equals(path, "static", StringComparison.Ordinal))
            return await Mediator.Send(request).ConfigureAwait(false);

        using var sp = BuildProvider();
        return await sp.GetRequiredService<IMediator>().Send(request, CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask<int> SendAsync(string path, FailingSpanProbe request)
    {
        if (string.Equals(path, "static", StringComparison.Ordinal))
            return await Mediator.Send(request).ConfigureAwait(false);

        using var sp = BuildProvider();
        return await sp.GetRequiredService<IMediator>().Send(request, CancellationToken.None).ConfigureAwait(false);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMediator().WithTelemetry();
        services.AddTransient<SpanProbeHandler>();
        services.AddTransient<FailingSpanProbeHandler>();
        return services.BuildServiceProvider();
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener;

        public List<Recorded<long>> RequestsTotal { get; } = new();
        public List<Recorded<double>> DurationsMs { get; } = new();

        public MetricCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal))
                        l.EnableMeasurementEvents(instrument);
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (string.Equals(instrument.Name, "mediator.requests_total", StringComparison.Ordinal))
                    RequestsTotal.Add(new Recorded<long>(value, tags.ToArray()));
            });
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                if (string.Equals(instrument.Name, "mediator.request_duration_ms", StringComparison.Ordinal))
                    DurationsMs.Add(new Recorded<double>(value, tags.ToArray()));
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    // A success carries no tags at all, so its series is the one it always was; a failure carries
    // exactly one, error.type. ErrorType fails the test on any other tag shape.
    private readonly record struct Recorded<T>(T Value, KeyValuePair<string, object?>[] Tags)
    {
        public string? ErrorType
        {
            get
            {
                if (Tags.Length == 0)
                    return null;
                var tag = Assert.Single(Tags);
                Assert.Equal("error.type", tag.Key);
                return Assert.IsType<string>(tag.Value);
            }
        }
    }
}
