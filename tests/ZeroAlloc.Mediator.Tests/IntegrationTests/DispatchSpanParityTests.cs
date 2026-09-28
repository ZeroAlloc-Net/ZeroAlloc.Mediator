using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct SpanParityStream(int Max) : IStreamRequest<int>;

public sealed class SpanParityStreamHandler : IStreamRequestHandler<SpanParityStream, int>
{
    public async IAsyncEnumerable<int> Handle(SpanParityStream request, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Max; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

// Its constructor throws, so creating the stream fails on both paths: the static path constructs
// it through the parameterless fallback, the IMediator path through the container.
public readonly record struct UnresolvableSpanStream(int Max) : IStreamRequest<int>;

public sealed class UnresolvableSpanStreamHandler : IStreamRequestHandler<UnresolvableSpanStream, int>
{
    public UnresolvableSpanStreamHandler() => throw new InvalidOperationException("handler construction failed");

    public async IAsyncEnumerable<int> Handle(UnresolvableSpanStream request, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return request.Max;
    }
}

public readonly record struct SpanParityRequest(int Value) : IRequest<int>;

public sealed class SpanParityRequestHandler : IRequestHandler<SpanParityRequest, int>
{
    public ValueTask<int> Handle(SpanParityRequest request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

public readonly record struct FailingSpanParityRequest(int Value) : IRequest<int>;

public sealed class FailingSpanParityRequestHandler : IRequestHandler<FailingSpanParityRequest, int>
{
    public ValueTask<int> Handle(FailingSpanParityRequest request, CancellationToken ct)
        => throw new TimeoutException("request failed");
}

public readonly record struct SpanParityNotification(int Value) : INotification;

public sealed class SpanParityNotificationHandler : INotificationHandler<SpanParityNotification>
{
    public ValueTask Handle(SpanParityNotification notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public readonly record struct FailingSpanParityNotification(int Value) : INotification;

public sealed class FailingSpanParityNotificationHandler : INotificationHandler<FailingSpanParityNotification>
{
    public ValueTask Handle(FailingSpanParityNotification notification, CancellationToken ct)
        => throw new InvalidOperationException("notification failed");
}

// #238: the static Mediator and the injected IMediator must emit the same span for every dispatch
// kind: same name, same tags, same error status. A span that ends in error also carries
// error.type, the exception's full type name, as the metrics do; a success span does not.
// These run without the Telemetry package, because the spans come from the generated dispatch
// methods, not from a pipeline behavior.
public class DispatchSpanParityTests
{
    public static TheoryData<string> Paths => new() { "static", "IMediator" };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Send_OpensExactlyOneSendSpan(string path)
    {
        using var capture = new SpanCapture();
        using var sp = BuildProvider();

        var result = string.Equals(path, "static", StringComparison.Ordinal)
            ? await Mediator.Send(new SpanParityRequest(41), CancellationToken.None)
            : await sp.GetRequiredService<IMediator>().Send(new SpanParityRequest(41), CancellationToken.None);

        Assert.Equal(42, result);
        var activity = Assert.Single(capture.Spans("mediator.send", "request.type", "SpanParityRequest"));
        Assert.Null(activity.Parent);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(
            new[] { new KeyValuePair<string, string?>("request.type", "SpanParityRequest") },
            activity.Tags.ToArray());
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Send_OnHandlerException_MarksTheSendSpanAsError_WithErrorType(string path)
    {
        using var capture = new SpanCapture();
        using var sp = BuildProvider();

        var thrown = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            if (string.Equals(path, "static", StringComparison.Ordinal))
                await Mediator.Send(new FailingSpanParityRequest(1), CancellationToken.None).ConfigureAwait(false);
            else
                await sp.GetRequiredService<IMediator>().Send(new FailingSpanParityRequest(1), CancellationToken.None).ConfigureAwait(false);
        });

        Assert.Equal("request failed", thrown.Message);
        var activity = Assert.Single(capture.Spans("mediator.send", "request.type", "FailingSpanParityRequest"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("request failed", activity.StatusDescription);
        Assert.Equal("System.TimeoutException", activity.GetTagItem("error.type"));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task CreateStream_OpensExactlyOneStreamSpan(string path)
    {
        using var capture = new SpanCapture();

        var values = new List<int>();
        using (var sp = BuildProvider())
        {
            var stream = string.Equals(path, "static", StringComparison.Ordinal)
                ? Mediator.CreateStream(new SpanParityStream(3), CancellationToken.None)
                : sp.GetRequiredService<IMediator>().CreateStream(new SpanParityStream(3), CancellationToken.None);
            await foreach (var v in stream)
                values.Add(v);
        }

        Assert.Equal([1, 2, 3], values);
        var activity = Assert.Single(capture.Spans("mediator.stream", "request.type", "SpanParityStream"));
        Assert.Null(activity.Parent);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(
            new[] { new KeyValuePair<string, string?>("request.type", "SpanParityStream") },
            activity.Tags.ToArray());
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void CreateStream_WhenTheHandlerCannotBeCreated_MarksTheStreamSpanAsError_WithErrorType(string path)
    {
        using var capture = new SpanCapture();
        using var sp = BuildProvider();

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            string.Equals(path, "static", StringComparison.Ordinal)
                ? Mediator.CreateStream(new UnresolvableSpanStream(1), CancellationToken.None)
                : sp.GetRequiredService<IMediator>().CreateStream(new UnresolvableSpanStream(1), CancellationToken.None));

        Assert.Equal("handler construction failed", thrown.Message);
        var activity = Assert.Single(capture.Spans("mediator.stream", "request.type", "UnresolvableSpanStream"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("handler construction failed", activity.StatusDescription);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("error.type"));
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Publish_OpensExactlyOnePublishSpan(string path)
    {
        using var capture = new SpanCapture();
        using var sp = BuildProvider();

        if (string.Equals(path, "static", StringComparison.Ordinal))
            await Mediator.Publish(new SpanParityNotification(1), CancellationToken.None);
        else
            await sp.GetRequiredService<IMediator>().Publish(new SpanParityNotification(1), CancellationToken.None);

        var activity = Assert.Single(capture.Spans("mediator.publish", "notification.type", "SpanParityNotification"));
        Assert.Null(activity.Parent);
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Equal(
            new[] { new KeyValuePair<string, string?>("notification.type", "SpanParityNotification") },
            activity.Tags.ToArray());
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Publish_OnHandlerException_MarksThePublishSpanAsError_WithErrorType(string path)
    {
        using var capture = new SpanCapture();
        using var sp = BuildProvider();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (string.Equals(path, "static", StringComparison.Ordinal))
                await Mediator.Publish(new FailingSpanParityNotification(1), CancellationToken.None).ConfigureAwait(false);
            else
                await sp.GetRequiredService<IMediator>().Publish(new FailingSpanParityNotification(1), CancellationToken.None).ConfigureAwait(false);
        });

        Assert.Equal("notification failed", thrown.Message);
        var activity = Assert.Single(capture.Spans("mediator.publish", "notification.type", "FailingSpanParityNotification"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("notification failed", activity.StatusDescription);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("error.type"));
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        services.AddTransient<SpanParityRequestHandler>();
        services.AddTransient<FailingSpanParityRequestHandler>();
        services.AddTransient<SpanParityStreamHandler>();
        services.AddTransient<UnresolvableSpanStreamHandler>();
        services.AddTransient<SpanParityNotificationHandler>();
        services.AddTransient<FailingSpanParityNotificationHandler>();
        return services.BuildServiceProvider();
    }

    // Other tests in this assembly run in parallel and emit spans on the same source, so each
    // test keeps only the spans tagged with its own message type.
    private sealed class SpanCapture : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _stopped = new();
        private readonly ActivityListener _listener;

        public SpanCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = src => string.Equals(src.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Spans(string name, string tag, string typeName)
            => _stopped.Where(a => string.Equals(a.OperationName, name, StringComparison.Ordinal)
                                && Equals(a.GetTagItem(tag), typeName)).ToList();

        public void Dispose() => _listener.Dispose();
    }
}
