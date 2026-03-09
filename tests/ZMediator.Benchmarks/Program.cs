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
    private ZMediator.IZMediator _zMediatorDi = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(static cfg =>
            cfg.RegisterServicesFromAssembly(typeof(MediatorBenchmarks).Assembly));
        services.AddSingleton<ZMediator.IZMediator, ZMediator.ZMediatorService>();
        var provider = services.BuildServiceProvider();
        _mediatR = provider.GetRequiredService<IMediator>();
        _zMediatorDi = provider.GetRequiredService<ZMediator.IZMediator>();
    }

    // === Request/Response ===

    [BenchmarkCategory("Send"), Benchmark(Baseline = true)]
    public ValueTask<string> ZMediator_Send()
        => ZMediator.Mediator.Send(new ZBenchPing("test"), _ct);

    [BenchmarkCategory("Send"), Benchmark]
    public Task<string> MediatR_Send()
        => _mediatR.Send(new MBenchPing("test"), _ct);

    // === Send with Pipeline ===

    [BenchmarkCategory("SendPipeline"), Benchmark(Baseline = true)]
    public ValueTask<int> ZMediator_SendPipeline()
        => ZMediator.Mediator.Send(new ZBenchCreateUser("test"), _ct);

    [BenchmarkCategory("SendPipeline"), Benchmark]
    public Task<int> MediatR_SendPipeline()
        => _mediatR.Send(new MBenchCreateUser("test"), _ct);

    // === Notification (single handler) ===

    [BenchmarkCategory("Publish1"), Benchmark(Baseline = true)]
    public ValueTask ZMediator_Publish_Single()
        => ZMediator.Mediator.Publish(new ZBenchEvent("test"), _ct);

    [BenchmarkCategory("Publish1"), Benchmark]
    public Task MediatR_Publish_Single()
        => _mediatR.Publish(new MBenchEvent("test"), _ct);

    // === Notification (multiple handlers) ===

    [BenchmarkCategory("Publish2"), Benchmark(Baseline = true)]
    public ValueTask ZMediator_Publish_Multi()
        => ZMediator.Mediator.Publish(new ZBenchMultiEvent(42), _ct);

    [BenchmarkCategory("Publish2"), Benchmark]
    public Task MediatR_Publish_Multi()
        => _mediatR.Publish(new MBenchMultiEvent(42), _ct);

    // === Streaming ===

    [BenchmarkCategory("Stream"), Benchmark(Baseline = true)]
    public async Task ZMediator_Stream()
    {
        await foreach (var _ in ZMediator.Mediator.CreateStream(new ZBenchStreamRequest(5), _ct))
        {
        }
    }

    [BenchmarkCategory("Stream"), Benchmark]
    public async Task MediatR_Stream()
    {
        await foreach (var _ in _mediatR.CreateStream(new MBenchStreamRequest(5), _ct))
        {
        }
    }

    // === DI Interface (IZMediator) ===

    [BenchmarkCategory("SendDI"), Benchmark(Baseline = true)]
    public ValueTask<string> ZMediator_Send_Static()
        => ZMediator.Mediator.Send(new ZBenchPing("test"), _ct);

    [BenchmarkCategory("SendDI"), Benchmark]
    public ValueTask<string> ZMediator_Send_DI()
        => _zMediatorDi.Send(new ZBenchPing("test"), _ct);

    [BenchmarkCategory("SendDI"), Benchmark]
    public Task<string> MediatR_Send_DI()
        => _mediatR.Send(new MBenchPing("test"), _ct);
}

// ============================================================
// ZMediator Types
// ============================================================

public readonly record struct ZBenchPing(string Message) : ZMediator.IRequest<string>;
public readonly record struct ZBenchCreateUser(string Name) : ZMediator.IRequest<int>;
public readonly record struct ZBenchEvent(string Data) : ZMediator.INotification;
public readonly record struct ZBenchMultiEvent(int Id) : ZMediator.INotification;
public readonly record struct ZBenchStreamRequest(int Count) : ZMediator.IStreamRequest<int>;

// ============================================================
// ZMediator Handlers
// ============================================================

public class ZBenchPingHandler : ZMediator.IRequestHandler<ZBenchPing, string>
{
    public ValueTask<string> Handle(ZBenchPing request, CancellationToken ct)
        => ValueTask.FromResult(request.Message);
}

public class ZBenchCreateUserHandler : ZMediator.IRequestHandler<ZBenchCreateUser, int>
{
    public ValueTask<int> Handle(ZBenchCreateUser request, CancellationToken ct)
        => ValueTask.FromResult(1);
}

public class ZBenchEventHandler : ZMediator.INotificationHandler<ZBenchEvent>
{
    public ValueTask Handle(ZBenchEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchMultiEventHandlerA : ZMediator.INotificationHandler<ZBenchMultiEvent>
{
    public ValueTask Handle(ZBenchMultiEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchMultiEventHandlerB : ZMediator.INotificationHandler<ZBenchMultiEvent>
{
    public ValueTask Handle(ZBenchMultiEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

public class ZBenchStreamHandler : ZMediator.IStreamRequestHandler<ZBenchStreamRequest, int>
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
// ZMediator Pipeline Behavior
// ============================================================

[ZMediator.PipelineBehavior(Order = 0, AppliesTo = typeof(ZBenchCreateUser))]
public class ZBenchLoggingBehavior : ZMediator.IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : ZMediator.IRequest<TResponse>
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
