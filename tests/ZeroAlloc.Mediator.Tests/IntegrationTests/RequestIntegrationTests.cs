using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// Handler must be public, top-level in the namespace
public readonly record struct IntegrationPing(string Message) : IRequest<string>;

public class IntegrationPingHandler : IRequestHandler<IntegrationPing, string>
{
    public ValueTask<string> Handle(IntegrationPing request, CancellationToken ct)
        => ValueTask.FromResult($"Pong: {request.Message}");
}

public readonly record struct IntegrationAdd(int A, int B) : IRequest<int>;

public class IntegrationAddHandler : IRequestHandler<IntegrationAdd, int>
{
    public ValueTask<int> Handle(IntegrationAdd request, CancellationToken ct)
        => ValueTask.FromResult(request.A + request.B);
}

public readonly record struct PipelineDiPing(string Message) : IRequest<string>;

public class PipelineDiPingHandler : IRequestHandler<PipelineDiPing, string>
{
    public ValueTask<string> Handle(PipelineDiPing request, CancellationToken ct)
        => ValueTask.FromResult(request.Message);
}

public readonly record struct ThrowPing(int X) : IRequest<string>;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "ZeroAlloc.Mediator", "ZAM008",
    Justification = "Test fixture deliberately omits parameterless ctor to verify the runtime InvalidOperationException thrown from the static Mediator.Send path.")]
public class ThrowPingHandler : IRequestHandler<ThrowPing, string>
{
    private readonly string _greeting;
    public ThrowPingHandler(string greeting) => _greeting = greeting;
    public ValueTask<string> Handle(ThrowPing r, CancellationToken ct)
        => ValueTask.FromResult(_greeting);
}

[PipelineBehavior(Order = 0, AppliesTo = typeof(PipelineDiPing))]
public class PipelineDiObservingBehavior : IPipelineBehavior
{
    public static int InvocationCount;
    public static System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        System.Func<TRequest, CancellationToken, System.Threading.Tasks.ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        System.Threading.Interlocked.Increment(ref InvocationCount);
        return next(request, ct);
    }
}

// === M3: scope flow through pipeline behaviors ===

public interface IScopedFlowMarker
{
    Guid Id { get; }
}

public sealed class ScopedFlowMarker : IScopedFlowMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

public readonly record struct ScopedFlowQuery : IRequest<Guid>;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "ZeroAlloc.Mediator", "ZAM008",
    Justification = "Test fixture exercised through DI (RegisterHandlersFromAssembly); never via static Mediator.Send.")]
public sealed class ScopedFlowHandler : IRequestHandler<ScopedFlowQuery, Guid>
{
    private readonly IScopedFlowMarker _marker;
    public ScopedFlowHandler(IScopedFlowMarker marker) => _marker = marker;
    public ValueTask<Guid> Handle(ScopedFlowQuery request, CancellationToken ct)
        => ValueTask.FromResult(_marker.Id);
}

[PipelineBehavior(Order = 1, AppliesTo = typeof(ScopedFlowQuery))]
public class ScopedFlowPassthroughBehavior : IPipelineBehavior
{
    public static System.Threading.Tasks.ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        System.Func<TRequest, CancellationToken, System.Threading.Tasks.ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => next(request, ct);
}

public readonly record struct ScopedFlowNoBehaviorQuery : IRequest<Guid>;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "ZeroAlloc.Mediator", "ZAM008",
    Justification = "Test fixture exercised through DI (RegisterHandlersFromAssembly); never via static Mediator.Send.")]
public sealed class ScopedFlowNoBehaviorHandler : IRequestHandler<ScopedFlowNoBehaviorQuery, Guid>
{
    private readonly IScopedFlowMarker _marker;
    public ScopedFlowNoBehaviorHandler(IScopedFlowMarker marker) => _marker = marker;
    public ValueTask<Guid> Handle(ScopedFlowNoBehaviorQuery request, CancellationToken ct)
        => ValueTask.FromResult(_marker.Id);
}

public class RequestIntegrationTests
{
    [Fact]
    public async Task Send_DispatchesToHandler()
    {
        var result = await Mediator.Send(new IntegrationPing("Hello"), CancellationToken.None);
        Assert.Equal("Pong: Hello", result);
    }

    [Fact]
    public async Task Send_ReturnsComputedResult()
    {
        var result = await Mediator.Send(new IntegrationAdd(3, 4), CancellationToken.None);
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task Send_ViaDi_ResolvesHandlerFromScope()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(IntegrationPingHandler).Assembly);

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new IntegrationPing("hi"), CancellationToken.None);
        Assert.Equal("Pong: hi", result);
    }

    [Fact]
    public async Task Send_ViaDi_AppliesPipelineBehaviors()
    {
        PipelineDiObservingBehavior.InvocationCount = 0;

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(PipelineDiPingHandler).Assembly);

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new PipelineDiPing("hello"), CancellationToken.None);

        Assert.Equal("hello", result);
        Assert.Equal(1, PipelineDiObservingBehavior.InvocationCount);
    }

    [Fact]
    public async Task StaticSend_NoFactory_NoParameterlessCtor_Throws()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mediator.Send(new ThrowPing(0), CancellationToken.None).AsTask());
        Assert.Contains("ThrowPingHandler", ex.Message);
        Assert.Contains("RegisterHandlersFromAssembly", ex.Message);
    }

    [Fact]
    public async Task Send_ViaDi_WithPipelineBehavior_FlowsCallerScopeIntoHandler()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped<IScopedFlowMarker, ScopedFlowMarker>();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScopedFlowHandler).Assembly);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        // Resolve the scoped marker in this scope first — capture its Id.
        var markerInScope = scope.ServiceProvider.GetRequiredService<IScopedFlowMarker>();
        var expected = markerInScope.Id;

        // Resolve the mediator in the SAME scope and Send. The pipeline behavior path uses
        // AsyncLocal<IServiceProvider> to thread the caller's scope through the static-lambda
        // chain emitted by PipelineEmitter; the handler's IScopedFlowMarker must resolve to
        // the SAME instance the caller saw.
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var fromHandler = await mediator.Send(new ScopedFlowQuery(), CancellationToken.None);

        Assert.Equal(expected, fromHandler);
    }

    [Fact]
    public async Task Send_ViaDi_NoPipelineBehavior_FlowsCallerScopeIntoHandler()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped<IScopedFlowMarker, ScopedFlowMarker>();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScopedFlowNoBehaviorHandler).Assembly);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var markerInScope = scope.ServiceProvider.GetRequiredService<IScopedFlowMarker>();
        var expected = markerInScope.Id;
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var fromHandler = await mediator.Send(new ScopedFlowNoBehaviorQuery(), CancellationToken.None);

        Assert.Equal(expected, fromHandler);
    }
}
