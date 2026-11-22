using System;
using System.Runtime.CompilerServices;
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

public readonly record struct HotPathDiPipelined(int Value) : IRequest<int>;

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

[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class HotPathDiPipelinedHandler : IRequestHandler<HotPathDiPipelined, int>
{
    public ValueTask<int> Handle(HotPathDiPipelined request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

// Two behaviors that await next and are not inlined, so the JIT cannot keep a delegate on the
// stack the way it does for HotPathForwardingBehavior. This is the case #308 measured at 248 B
// per IMediator.Send: before the provider was threaded through next, each behavior cost a delegate.
[PipelineBehavior(Order = 5, AppliesTo = typeof(HotPathDiPipelined))]
public sealed class HotPathAwaitingBehaviorA : IPipelineBehavior
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request, TState state, CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => await next(request, state, ct).ConfigureAwait(false);
}

[PipelineBehavior(Order = 6, AppliesTo = typeof(HotPathDiPipelined))]
public sealed class HotPathAwaitingBehaviorB : IPipelineBehavior
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request, TState state, CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => await next(request, state, ct).ConfigureAwait(false);
}

// No SetFactory for these: they pin the generated fallback for a stateless handler. Handle is not
// inlinable, so the handler instance escapes into the call and the JIT cannot stack-allocate it;
// a per-call `new` would show at every tier, with or without DOTNET_TieredCompilation.
public readonly record struct HotPathStatelessEvent(int Id) : INotification;

public readonly record struct HotPathStatelessRequest(int Value) : IRequest<int>;

public sealed class HotPathStatelessEventHandler : INotificationHandler<HotPathStatelessEvent>
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public ValueTask Handle(HotPathStatelessEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed class HotPathStatelessRequestHandler : IRequestHandler<HotPathStatelessRequest, int>
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public ValueTask<int> Handle(HotPathStatelessRequest request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
}

// Forwards to next, like the benchmark's behavior, and reads no services.
[PipelineBehavior(Order = 4, AppliesTo = typeof(HotPathPipelined))]
public sealed class HotPathForwardingBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request, TState state, CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => next(request, state, ct);
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
// The README fixtures get their handlers through SetFactory. The Stateless gates register no
// factory: the generated fallback caches one instance of a handler without instance fields, so
// the static path allocates nothing without any configuration. A handler that holds state is
// still created per call; see StaticHandlerCacheTests.
//
// IMediator.Send + pipeline is gated at 0 B too: the provider is the state threaded through
// next, so every lambda of the chain is static and no delegate is allocated per call.
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

    // Two awaiting behaviors, bridges included, through the generated IMediator.Send. Before the
    // provider was the pipeline state this allocated one delegate per behavior per call.
    [Fact]
    public void IMediatorSend_ThroughTwoAwaitingBehaviors_AllocatesNothing()
    {
        var mediator = _mediator;
        AllocationGate.AssertBudgetValueTask(0, Iterations,
            () => mediator.Send(new HotPathDiPipelined(41), CancellationToken.None),
            "IMediator.Send + two awaiting behaviors");
    }

    [Fact]
    public void StaticPublish_StatelessHandlerWithoutFactory_AllocatesNothing()
        => AllocationGate.AssertBudget(0, Iterations,
            static () => Complete(Mediator.Publish(new HotPathStatelessEvent(1), CancellationToken.None)),
            "Mediator.Publish, stateless handler, no factory");

    [Fact]
    public void StaticSend_StatelessHandlerWithoutFactory_AllocatesNothing()
        => AllocationGate.AssertBudgetValueTask(0, Iterations,
            static () => Mediator.Send(new HotPathStatelessRequest(41), CancellationToken.None),
            "Mediator.Send, stateless handler, no factory");

    private static void Complete(ValueTask task)
    {
        if (!task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Dispatch did not complete synchronously.");
        task.GetAwaiter().GetResult();
    }
}
