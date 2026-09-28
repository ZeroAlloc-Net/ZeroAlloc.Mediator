using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct UntracedSpanProbe(int Value) : IRequest<int>;

public sealed class UntracedSpanProbeHandler : IRequestHandler<UntracedSpanProbe, int>
{
    public ValueTask<int> Handle(UntracedSpanProbe request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

// Companion to the Telemetry package's SendSpanTests for #236: this project does NOT reference
// ZeroAlloc.Mediator.Telemetry, and the generated Send alone must still open exactly one span.
public class SendSpanIntegrationTests
{
    public static TheoryData<string> Paths => new() { "static", "IMediator" };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Send_OpensExactlyOneSpan_WithoutTelemetryPackage(string path)
    {
        // Other tests in this assembly run in parallel and emit their own spans on the same
        // source, so keep only the spans this test's request produced.
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => string.Equals(src.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        var result = await SendAsync(path, new UntracedSpanProbe(41));

        Assert.Equal(42, result);
        var mine = stopped.Where(a => string.Equals(a.OperationName, "mediator.send", StringComparison.Ordinal)
                                   && Equals(a.GetTagItem("request.type"), "UntracedSpanProbe")).ToList();
        var activity = Assert.Single(mine);
        Assert.Null(activity.Parent);
    }

    private static async ValueTask<int> SendAsync(string path, UntracedSpanProbe request)
    {
        if (string.Equals(path, "static", StringComparison.Ordinal))
            return await Mediator.Send(request).ConfigureAwait(false);

        var services = new ServiceCollection();
        services.AddMediator();
        services.AddTransient<UntracedSpanProbeHandler>();
        using var sp = services.BuildServiceProvider();
        return await sp.GetRequiredService<IMediator>().Send(request, CancellationToken.None).ConfigureAwait(false);
    }
}
