using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZeroAlloc.Mediator.Telemetry.Tests;

internal sealed class TestActivityListener : IDisposable
{
    private readonly ActivityListener _listener;
    public List<Activity> StoppedActivities { get; } = new();

    public TestActivityListener(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = src => string.Equals(src.Name, sourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = StoppedActivities.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}
