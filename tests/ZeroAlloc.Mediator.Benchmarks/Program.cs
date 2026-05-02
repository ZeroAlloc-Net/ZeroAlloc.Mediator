using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

BenchmarkRunner.Run<MediatorBenchmarks>(
    DefaultConfig.Instance
        .HideColumns(Column.Error, Column.StdDev, Column.Median, Column.RatioSD));

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MediatorBenchmarks
{
    private readonly CancellationToken _ct = CancellationToken.None;
    private IMediator _mediatR = null!;
    private IServiceProvider _provider = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(static cfg =>
            cfg.RegisterServicesFromAssembly(typeof(MediatorBenchmarks).Assembly));
        ZeroAlloc.Mediator.MediatorBuilderExtensions.RegisterHandlersFromAssembly(
            services.AddMediator(),
            typeof(MediatorBenchmarks).Assembly);
        _provider = services.BuildServiceProvider();
        _scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        _mediatR = _provider.GetRequiredService<IMediator>();
    }

    // === Request/Response ===

    [BenchmarkCategory("Send"), Benchmark(Baseline = true)]
    public ValueTask<string> ZeroAllocMediator_Send()
        => ZeroAlloc.Mediator.Mediator.Send(new ZBenchPing("test"), _ct);

    [BenchmarkCategory("Send"), Benchmark]
    public Task<string> MediatR_Send()
        => _mediatR.Send(new MBenchPing("test"), _ct);

    // === Send with Pipeline ===

    [BenchmarkCategory("SendPipeline"), Benchmark(Baseline = true)]
    public ValueTask<int> ZeroAllocMediator_SendPipeline()
        => ZeroAlloc.Mediator.Mediator.Send(new ZBenchCreateUser("test"), _ct);

    [BenchmarkCategory("SendPipeline"), Benchmark]
    public Task<int> MediatR_SendPipeline()
        => _mediatR.Send(new MBenchCreateUser("test"), _ct);

    // === Notification (single handler) ===

    [BenchmarkCategory("Publish1"), Benchmark(Baseline = true)]
    public ValueTask ZeroAllocMediator_Publish_Single()
        => ZeroAlloc.Mediator.Mediator.Publish(new ZBenchEvent("test"), _ct);

    [BenchmarkCategory("Publish1"), Benchmark]
    public Task MediatR_Publish_Single()
        => _mediatR.Publish(new MBenchEvent("test"), _ct);

    // === Notification (multiple handlers) ===

    [BenchmarkCategory("Publish2"), Benchmark(Baseline = true)]
    public ValueTask ZeroAllocMediator_Publish_Multi()
        => ZeroAlloc.Mediator.Mediator.Publish(new ZBenchMultiEvent(42), _ct);

    [BenchmarkCategory("Publish2"), Benchmark]
    public Task MediatR_Publish_Multi()
        => _mediatR.Publish(new MBenchMultiEvent(42), _ct);

    // === Streaming ===

    [BenchmarkCategory("Stream"), Benchmark(Baseline = true)]
    public async Task ZeroAllocMediator_Stream()
    {
        await foreach (var _ in ZeroAlloc.Mediator.Mediator.CreateStream(new ZBenchStreamRequest(5), _ct).ConfigureAwait(false))
        {
        }
    }

    [BenchmarkCategory("Stream"), Benchmark]
    public async Task MediatR_Stream()
    {
        await foreach (var _ in _mediatR.CreateStream(new MBenchStreamRequest(5), _ct).ConfigureAwait(false))
        {
        }
    }

    // === DI Send (scope-per-call models real ASP.NET request) ===

    [BenchmarkCategory("Send_DI"), Benchmark(Baseline = true)]
    public ValueTask<string> ZeroAllocMediator_Send_Static()
        => ZeroAlloc.Mediator.Mediator.Send(new ZBenchPing("test"), _ct);

    [BenchmarkCategory("Send_DI"), Benchmark]
    public async ValueTask<string> ZeroAllocMediator_Send_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>();
        return await m.Send(new ZBenchPing("test"), _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("Send_DI"), Benchmark]
    public async Task<string> MediatR_Send_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await m.Send(new MBenchPing("test"), _ct).ConfigureAwait(false);
    }

    // === DI Send with pipeline behavior ===

    [BenchmarkCategory("SendPipeline_DI"), Benchmark(Baseline = true)]
    public ValueTask<int> ZeroAllocMediator_SendPipeline_Static()
        => ZeroAlloc.Mediator.Mediator.Send(new ZBenchCreateUser("test"), _ct);

    [BenchmarkCategory("SendPipeline_DI"), Benchmark]
    public async ValueTask<int> ZeroAllocMediator_SendPipeline_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>();
        return await m.Send(new ZBenchCreateUser("test"), _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("SendPipeline_DI"), Benchmark]
    public async Task<int> MediatR_SendPipeline_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await m.Send(new MBenchCreateUser("test"), _ct).ConfigureAwait(false);
    }

    // === DI Publish (single handler) ===

    [BenchmarkCategory("Publish1_DI"), Benchmark(Baseline = true)]
    public ValueTask ZeroAllocMediator_Publish1_Static()
        => ZeroAlloc.Mediator.Mediator.Publish(new ZBenchEvent("test"), _ct);

    [BenchmarkCategory("Publish1_DI"), Benchmark]
    public async ValueTask ZeroAllocMediator_Publish1_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>();
        await m.Publish(new ZBenchEvent("test"), _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("Publish1_DI"), Benchmark]
    public async Task MediatR_Publish1_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        await m.Publish(new MBenchEvent("test"), _ct).ConfigureAwait(false);
    }

    // === DI Publish (multi handler) ===

    [BenchmarkCategory("Publish2_DI"), Benchmark(Baseline = true)]
    public ValueTask ZeroAllocMediator_Publish2_Static()
        => ZeroAlloc.Mediator.Mediator.Publish(new ZBenchMultiEvent(42), _ct);

    [BenchmarkCategory("Publish2_DI"), Benchmark]
    public async ValueTask ZeroAllocMediator_Publish2_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>();
        await m.Publish(new ZBenchMultiEvent(42), _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("Publish2_DI"), Benchmark]
    public async Task MediatR_Publish2_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        await m.Publish(new MBenchMultiEvent(42), _ct).ConfigureAwait(false);
    }

    // === DI Stream ===

    [BenchmarkCategory("Stream_DI"), Benchmark(Baseline = true)]
    public async Task ZeroAllocMediator_Stream_Static()
    {
        await foreach (var _ in ZeroAlloc.Mediator.Mediator
            .CreateStream(new ZBenchStreamRequest(5), _ct).ConfigureAwait(false)) { }
    }

    [BenchmarkCategory("Stream_DI"), Benchmark]
    public async Task ZeroAllocMediator_Stream_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<ZeroAlloc.Mediator.IMediator>();
        await foreach (var _ in m.CreateStream(new ZBenchStreamRequest(5), _ct).ConfigureAwait(false)) { }
    }

    [BenchmarkCategory("Stream_DI"), Benchmark]
    public async Task MediatR_Stream_DI()
    {
        using var scope = _scopeFactory.CreateScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        await foreach (var _ in m.CreateStream(new MBenchStreamRequest(5), _ct).ConfigureAwait(false)) { }
    }
}

// ============================================================
// ZeroAlloc.Mediator Types
// ============================================================

public readonly record struct ZBenchPing(string Message) : ZeroAlloc.Mediator.IRequest<string>;
public readonly record struct ZBenchCreateUser(string Name) : ZeroAlloc.Mediator.IRequest<int>;
public readonly record struct ZBenchEvent(string Data) : ZeroAlloc.Mediator.INotification;
public readonly record struct ZBenchMultiEvent(int Id) : ZeroAlloc.Mediator.INotification;
public readonly record struct ZBenchStreamRequest(int Count) : ZeroAlloc.Mediator.IStreamRequest<int>;

// ============================================================
// ZeroAlloc.Mediator Handlers
// ============================================================

public class ZBenchPingHandler : ZeroAlloc.Mediator.IRequestHandler<ZBenchPing, string>
{
    public ValueTask<string> Handle(ZBenchPing request, CancellationToken ct)
        => ValueTask.FromResult(request.Message);
}

public class ZBenchCreateUserHandler : ZeroAlloc.Mediator.IRequestHandler<ZBenchCreateUser, int>
{
    public ValueTask<int> Handle(ZBenchCreateUser request, CancellationToken ct)
        => ValueTask.FromResult(1);
}

public class ZBenchEventHandler : ZeroAlloc.Mediator.INotificationHandler<ZBenchEvent>
{
    public ValueTask Handle(ZBenchEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchMultiEventHandlerA : ZeroAlloc.Mediator.INotificationHandler<ZBenchMultiEvent>
{
    public ValueTask Handle(ZBenchMultiEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchMultiEventHandlerB : ZeroAlloc.Mediator.INotificationHandler<ZBenchMultiEvent>
{
    public ValueTask Handle(ZBenchMultiEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchStreamHandler : ZeroAlloc.Mediator.IStreamRequestHandler<ZBenchStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        ZBenchStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
    }
}

// ============================================================
// ZeroAlloc.Mediator Pipeline Behavior
// ============================================================

[ZeroAlloc.Mediator.PipelineBehavior(Order = 0, AppliesTo = typeof(ZBenchCreateUser))]
public class ZBenchLoggingBehavior : ZeroAlloc.Mediator.IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : ZeroAlloc.Mediator.IRequest<TResponse>
    {
        return next(request, ct);
    }
}

// ============================================================
// MediatR Types
// ============================================================

public record MBenchPing(string Message) : IRequest<string>;
public record MBenchCreateUser(string Name) : IRequest<int>;
public record MBenchEvent(string Data) : MediatR.INotification;
public record MBenchMultiEvent(int Id) : MediatR.INotification;
public record MBenchStreamRequest(int Count) : IStreamRequest<int>;

// ============================================================
// MediatR Handlers
// ============================================================

public class MBenchPingHandler : IRequestHandler<MBenchPing, string>
{
    public Task<string> Handle(MBenchPing request, CancellationToken ct)
        => Task.FromResult(request.Message);
}

public class MBenchCreateUserHandler : IRequestHandler<MBenchCreateUser, int>
{
    public Task<int> Handle(MBenchCreateUser request, CancellationToken ct)
        => Task.FromResult(1);
}

public class MBenchEventHandler : MediatR.INotificationHandler<MBenchEvent>
{
    public Task Handle(MBenchEvent notification, CancellationToken ct)
        => Task.CompletedTask;
}

public class MBenchMultiEventHandlerA : MediatR.INotificationHandler<MBenchMultiEvent>
{
    public Task Handle(MBenchMultiEvent notification, CancellationToken ct)
        => Task.CompletedTask;
}

public class MBenchMultiEventHandlerB : MediatR.INotificationHandler<MBenchMultiEvent>
{
    public Task Handle(MBenchMultiEvent notification, CancellationToken ct)
        => Task.CompletedTask;
}

public class MBenchStreamHandler : IStreamRequestHandler<MBenchStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        MBenchStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
    }
}

// ============================================================
// MediatR Pipeline Behavior
// ============================================================

public class MBenchLoggingBehavior : IPipelineBehavior<MBenchCreateUser, int>
{
    public Task<int> Handle(MBenchCreateUser request, RequestHandlerDelegate<int> next, CancellationToken ct)
        => next();
}
