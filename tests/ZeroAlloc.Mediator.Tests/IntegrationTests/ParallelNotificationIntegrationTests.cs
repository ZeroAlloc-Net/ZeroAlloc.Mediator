using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

[ParallelNotification]
public readonly record struct ParallelProbe(int Id) : INotification;

// Three handlers whose results each test scripts per slot. Singletons, so the DI path resolves
// without allocating and the allocation test measures Publish alone.
[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class ParallelProbeHandlerA : INotificationHandler<ParallelProbe>
{
    public ValueTask Handle(ParallelProbe notification, CancellationToken ct) => ParallelProbeScript.Run(0);
}

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class ParallelProbeHandlerB : INotificationHandler<ParallelProbe>
{
    public ValueTask Handle(ParallelProbe notification, CancellationToken ct) => ParallelProbeScript.Run(1);
}

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class ParallelProbeHandlerC : INotificationHandler<ParallelProbe>
{
    public ValueTask Handle(ParallelProbe notification, CancellationToken ct) => ParallelProbeScript.Run(2);
}

internal static class ParallelProbeScript
{
    // What each handler slot returns; null returns a successfully completed ValueTask.
    public static readonly Func<ValueTask>?[] Results = new Func<ValueTask>?[3];
    public static readonly int[] Calls = new int[3];

    public static void Reset()
    {
        Array.Clear(Results);
        Array.Clear(Calls);
    }

    public static ValueTask Run(int slot)
    {
        Interlocked.Increment(ref Calls[slot]);
        return Results[slot]?.Invoke() ?? ValueTask.CompletedTask;
    }
}

// An already-succeeded IValueTaskSource that counts how often its result is consumed.
internal sealed class CountingValueTaskSource : IValueTaskSource
{
    public int GetResultCalls;

    public void GetResult(short token) => Interlocked.Increment(ref GetResultCalls);

    public ValueTaskSourceStatus GetStatus(short token) => ValueTaskSourceStatus.Succeeded;

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => continuation(state);
}

// Runs alone: the span tests attach an ActivityListener to the mediator's ActivitySource, and
// while one is attached every Publish allocates its Activity, which the allocation test would
// count.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParallelNotificationCollection
{
    public const string Name = "parallel-notification";
}

// Pins what [ParallelNotification] Publish does for every mix of handler results, on the static
// Mediator and on IMediator. Handlers run in declaration order: A, B, C.
[Collection(ParallelNotificationCollection.Name)]
public sealed class ParallelNotificationIntegrationTests : IDisposable
{
    private static readonly ParallelProbeHandlerA s_a = new();
    private static readonly ParallelProbeHandlerB s_b = new();
    private static readonly ParallelProbeHandlerC s_c = new();

    private readonly ServiceProvider _provider;
    private readonly IMediator _mediator;

    public ParallelNotificationIntegrationTests()
    {
        ParallelProbeScript.Reset();

        // Cached instances, so the static path allocates no handler per call.
        Mediator.Configure(c =>
        {
            c.SetFactory(() => s_a);
            c.SetFactory(() => s_b);
            c.SetFactory(() => s_c);
        });

        var services = new ServiceCollection();
        services.AddMediator();
        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<IMediator>();
    }

    public void Dispose()
    {
        ParallelProbeScript.Reset();
        _provider.Dispose();
    }

    private ValueTask Publish(bool viaDi)
        => viaDi
            ? _mediator.Publish(new ParallelProbe(1), CancellationToken.None)
            : Mediator.Publish(new ParallelProbe(1), CancellationToken.None);

    private static void AssertEveryHandlerRanOnce()
        => Assert.Equal(new[] { 1, 1, 1 }, ParallelProbeScript.Calls);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllSucceedSynchronously_CompletesSynchronously(bool viaDi)
    {
        var publish = Publish(viaDi);

        Assert.True(publish.IsCompletedSuccessfully);
        await publish;
        AssertEveryHandlerRanOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllSucceedSynchronously_AllocatesNothing(bool viaDi)
    {
        AllocationGate.AssertBudget(0, 1000, () =>
        {
            var publish = Publish(viaDi);
            if (!publish.IsCompletedSuccessfully)
                throw new InvalidOperationException("Publish did not complete synchronously.");
            publish.GetAwaiter().GetResult();
        }, viaDi ? "IMediator.Publish [ParallelNotification]" : "Mediator.Publish [ParallelNotification]");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneFaulted_ThrowsItsException_AfterRunningEveryHandler(bool viaDi)
    {
        var boom = new InvalidOperationException("B");
        ParallelProbeScript.Results[1] = () => ValueTask.FromException(boom);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(viaDi).AsTask());

        Assert.Same(boom, thrown);
        AssertEveryHandlerRanOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoFaulted_SurfaceOnlyTheFirstRegistered(bool viaDi)
    {
        var first = new InvalidOperationException("A");
        var second = new InvalidOperationException("C");
        ParallelProbeScript.Results[0] = () => ValueTask.FromException(first);
        ParallelProbeScript.Results[2] = () => ValueTask.FromException(second);

        var task = Publish(viaDi).AsTask();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => task);

        // The async Publish rethrows what awaiting Task.WhenAll throws: the first exception only.
        Assert.Same(first, thrown);
        Assert.Single(task.Exception!.InnerExceptions);
        AssertEveryHandlerRanOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneCanceled_CancelsThePublish(bool viaDi)
    {
        ParallelProbeScript.Results[1] = () => ValueTask.FromCanceled(new CancellationToken(canceled: true));

        var task = Publish(viaDi).AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

        Assert.True(task.IsCanceled);
        AssertEveryHandlerRanOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultedAndCanceled_FaultWins(bool viaDi)
    {
        var boom = new InvalidOperationException("C");
        ParallelProbeScript.Results[0] = () => ValueTask.FromCanceled(new CancellationToken(canceled: true));
        ParallelProbeScript.Results[2] = () => ValueTask.FromException(boom);

        var task = Publish(viaDi).AsTask();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => task);

        Assert.Same(boom, thrown);
        Assert.True(task.IsFaulted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SucceededAndPending_CompletesWhenThePendingOneDoes(bool viaDi)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ParallelProbeScript.Results[1] = () => new ValueTask(pending.Task);

        var task = Publish(viaDi).AsTask();
        Assert.False(task.IsCompleted);

        pending.SetResult();
        await task;
        AssertEveryHandlerRanOnce();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultedAndPending_WaitsForThePendingOne_ThenThrows(bool viaDi)
    {
        var boom = new InvalidOperationException("A");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ParallelProbeScript.Results[0] = () => ValueTask.FromException(boom);
        ParallelProbeScript.Results[2] = () => new ValueTask(pending.Task);

        var task = Publish(viaDi).AsTask();
        Assert.False(task.IsCompleted);

        pending.SetResult();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Same(boom, thrown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingFaultBeforeSynchronousFault_TheAlreadyFaultedOneWins(bool viaDi)
    {
        var pendingBoom = new InvalidOperationException("A");
        var syncBoom = new InvalidOperationException("C");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ParallelProbeScript.Results[0] = () => new ValueTask(pending.Task);
        ParallelProbeScript.Results[2] = () => ValueTask.FromException(syncBoom);

        var task = Publish(viaDi).AsTask();
        pending.SetException(pendingBoom);

        // Task.WhenAll records faults in completion order, so registration order does not decide.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Same(syncBoom, thrown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandlerThrowingBeforeReturning_StopsTheFanOut(bool viaDi)
    {
        var boom = new InvalidOperationException("B");
        ParallelProbeScript.Results[1] = () => throw boom;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(viaDi).AsTask());

        Assert.Same(boom, thrown);
        Assert.Equal(new[] { 1, 1, 0 }, ParallelProbeScript.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValueTaskSourceResults_AreConsumedExactlyOnce(bool viaDi)
    {
        var sources = new[] { new CountingValueTaskSource(), new CountingValueTaskSource(), new CountingValueTaskSource() };
        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            ParallelProbeScript.Results[i] = () => new ValueTask(source, 0);
        }

        await Publish(viaDi);

        Assert.All(sources, s => Assert.Equal(1, s.GetResultCalls));
    }
}
