using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// Fixtures mirroring the README benchmark rows: sequential Publish to one and to several handlers,
// and Send through a pipeline behavior. Singletons, so the DI path resolves handlers without
// allocating and the gates measure dispatch alone.
public readonly record struct HotPathEvent(int Id) : INotification;

public readonly record struct HotPathMultiEvent(int Id) : INotification;

public readonly record struct HotPathPipelined(int Value) : IRequest<int>;

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class HotPathEventHandler : INotificationHandler<HotPathEvent>
{
    public ValueTask Handle(HotPathEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class HotPathMultiEventHandlerA : INotificationHandler<HotPathMultiEvent>
{
    public ValueTask Handle(HotPathMultiEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class HotPathMultiEventHandlerB : INotificationHandler<HotPathMultiEvent>
{
    public ValueTask Handle(HotPathMultiEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class HotPathPipelinedHandler : IRequestHandler<HotPathPipelined, int>
{
    public ValueTask<int> Handle(HotPathPipelined request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

// Forwards to next, like the benchmark's behavior, and reads no services.
[PipelineBehavior(Order = 4, AppliesTo = typeof(HotPathPipelined), UsesPipelineBehaviorServices = false)]
public sealed class HotPathForwardingBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => next(request, ct);
}

// Runs alone: a span-capturing test attaching an ActivityListener to the mediator's
// ActivitySource would make every dispatch allocate its Activity.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HotPathAllocationCollection
{
    public const string Name = "hot-path-allocation";
}

// Pins the 0 B the README and docs/performance.md claim for sequential Publish, on the static
// Mediator and on IMediator, and for Send + pipeline on the static Mediator. Each gate measures the
// whole public call.
//
// The static path gets cached handlers through SetFactory. Without a factory the generated
// fallback news up the handler on every call; whether that allocation shows depends on the JIT
// eliding it, so it cannot be gated here. See the README's performance notes.
//
// IMediator.Send + pipeline is not gated: its next delegates read the resolving provider, so
// they allocate unless the JIT inlines them, tracked in ZeroAlloc.Pipeline#121.
[Collection(HotPathAllocationCollection.Name)]
public sealed class HotPathAllocationTests : IDisposable
{
    private const int Iterations = 1000;

    private static readonly HotPathEventHandler s_event = new();
    private static readonly HotPathMultiEventHandlerA s_multiA = new();
    private static readonly HotPathMultiEventHandlerB s_multiB = new();
    private static readonly HotPathPipelinedHandler s_pipelined = new();

    private readonly ServiceProvider _provider;
    private readonly IMediator _mediator;

    public HotPathAllocationTests()
    {
        Mediator.Configure(static c =>
        {
            c.SetFactory(static () => s_event);
            c.SetFactory(static () => s_multiA);
            c.SetFactory(static () => s_multiB);
            c.SetFactory(static () => s_pipelined);
        });

        var services = new ServiceCollection();
        services.AddMediator();
        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<IMediator>();
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_SingleHandler_AllocatesNothing(bool viaDi)
        => AllocationGate.AssertBudget(0, Iterations, () =>
        {
            var publish = viaDi
                ? _mediator.Publish(new HotPathEvent(1), CancellationToken.None)
                : Mediator.Publish(new HotPathEvent(1), CancellationToken.None);
            Complete(publish);
        }, viaDi ? "IMediator.Publish, one handler" : "Mediator.Publish, one handler");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_MultipleHandlers_AllocatesNothing(bool viaDi)
        => AllocationGate.AssertBudget(0, Iterations, () =>
        {
            var publish = viaDi
                ? _mediator.Publish(new HotPathMultiEvent(1), CancellationToken.None)
                : Mediator.Publish(new HotPathMultiEvent(1), CancellationToken.None);
            Complete(publish);
        }, viaDi ? "IMediator.Publish, two handlers" : "Mediator.Publish, two handlers");

    // This assembly also references the Cache, Validation and Resilience bridges, whose behaviors
    // join the chain; the static path stays at 0 B with all of them.
    [Fact]
    public void StaticSend_ThroughPipelineBehavior_AllocatesNothing()
        => AllocationGate.AssertBudgetValueTask(0, Iterations,
            static () => Mediator.Send(new HotPathPipelined(41), CancellationToken.None),
            "Mediator.Send + pipeline");

    private static void Complete(ValueTask task)
    {
        if (!task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Dispatch did not complete synchronously.");
        task.GetAwaiter().GetResult();
    }
}
