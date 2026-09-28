using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Telemetry;

namespace ZeroAlloc.Mediator.Telemetry.Tests;

// Disable parallelism — the generated Send and TelemetryBehavior emit on process-wide static ActivitySource/Meter
// instances; concurrent tests would observe each other's activities and metric records.
[CollectionDefinition("telemetry-non-parallel", DisableParallelization = true)]
public sealed class TelemetryNonParallelCollection { }

// Stub IRequest + handler — every IRequest<T> needs a registered handler to satisfy
// the generator's ZAM001 diagnostic. The handler is never invoked because the tests
// call TelemetryBehavior.Handle directly with an inline `next` lambda. SendSpanTests
// covers the behavior inside the generated Send.
public readonly record struct TestRequest(int Value) : IRequest<int>;

public sealed class TestRequestHandler : IRequestHandler<TestRequest, int>
{
    public ValueTask<int> Handle(TestRequest request, CancellationToken ct) => ValueTask.FromResult(0);
}

[Collection("telemetry-non-parallel")]
public class TelemetryBehaviorTests
{
    [Fact]
    public async Task Handle_DoesNotStartActivity()
    {
        // The generated Send owns the mediator.send span (#236); the behavior only records metrics.
        using var listener = new TestActivityListener("ZeroAlloc.Mediator");

        var result = await TelemetryBehavior.Handle<TestRequest, int>(
            new TestRequest(7),
            CancellationToken.None,
            (r, _) => ValueTask.FromResult(r.Value * 2));

        Assert.Equal(14, result);
        Assert.Empty(listener.StoppedActivities);
    }

    [Fact]
    public async Task Handle_OnException_Propagates_WithoutStartingActivity()
    {
        using var listener = new TestActivityListener("ZeroAlloc.Mediator");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TelemetryBehavior.Handle<TestRequest, int>(
                new TestRequest(1),
                CancellationToken.None,
                (_, _) => throw new InvalidOperationException("boom")).AsTask());

        Assert.Equal("boom", thrown.Message);
        Assert.Empty(listener.StoppedActivities);
    }

    [Fact]
    public async Task Handle_OnSuccess_IncrementsRequestsTotalCounter()
    {
        var measurements = new List<long>();
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
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            measurements.Add(value);
        });
        meterListener.Start();

        await TelemetryBehavior.Handle<TestRequest, int>(
            new TestRequest(1),
            CancellationToken.None,
            (_, _) => ValueTask.FromResult(0));

        await TelemetryBehavior.Handle<TestRequest, int>(
            new TestRequest(2),
            CancellationToken.None,
            (_, _) => ValueTask.FromResult(0));

        Assert.Equal(2, measurements.Count);
        Assert.All(measurements, m => Assert.Equal(1L, m));
    }

    [Fact]
    public async Task Handle_OnException_CountsTheRequest_TaggedWithErrorType()
    {
        // #238: requests_total counts every request; a failure carries error.type on both
        // instruments, a success carries no tags.
        var recorded = new List<(string Instrument, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal))
                    l.EnableMeasurementEvents(instrument);
            },
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => recorded.Add((instrument.Name, tags.ToArray())));
        meterListener.Start();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            TelemetryBehavior.Handle<TestRequest, int>(
                new TestRequest(1),
                CancellationToken.None,
                (_, _) => throw new TimeoutException("slow")).AsTask());

        var errorTag = new KeyValuePair<string, object?>("error.type", "System.TimeoutException");
        Assert.Equal(
            new[] { ("mediator.requests_total", errorTag), ("mediator.request_duration_ms", errorTag) },
            recorded.Select(r => (r.Instrument, Assert.Single(r.Tags))).OrderByDescending(r => r.Instrument, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Handle_RecordsRequestDurationHistogram()
    {
        var measurements = new List<double>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal)
                    && string.Equals(instrument.Name, "mediator.request_duration_ms", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            measurements.Add(value);
        });
        meterListener.Start();

        await TelemetryBehavior.Handle<TestRequest, int>(
            new TestRequest(1),
            CancellationToken.None,
            (_, _) => ValueTask.FromResult(0));

        Assert.Single(measurements);
        Assert.True(measurements[0] >= 0.0, $"expected non-negative duration, got {measurements[0]}");
    }

    [Fact]
    public void WithTelemetry_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddMediator();

        var returned = builder.WithTelemetry();

        Assert.Same(builder, returned);
    }

    [Fact]
    public void WithTelemetry_IsIdempotent()
    {
        var services = new ServiceCollection();
        var builder = services.AddMediator();

        var first = builder.WithTelemetry();
        var second = builder.WithTelemetry();

        Assert.Same(builder, first);
        Assert.Same(builder, second);
        Assert.Same(first, second);
    }
}
