using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ZMediator;

BenchmarkRunner.Run<MediatorBenchmarks>();

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private readonly CancellationToken _ct = CancellationToken.None;

    [Benchmark]
    public ValueTask<string> Send_SimpleRequest()
    {
        return Mediator.Send(new BenchPing("test"), _ct);
    }

    [Benchmark]
    public ValueTask Publish_SingleHandler()
    {
        return Mediator.Publish(new BenchEvent("test"), _ct);
    }
}

public readonly record struct BenchPing(string Message) : IRequest<string>;
public readonly record struct BenchEvent(string Data) : INotification;

public class BenchPingHandler : IRequestHandler<BenchPing, string>
{
    public ValueTask<string> Handle(BenchPing request, CancellationToken ct)
        => ValueTask.FromResult(request.Message);
}

public class BenchEventHandler : INotificationHandler<BenchEvent>
{
    public ValueTask Handle(BenchEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}
