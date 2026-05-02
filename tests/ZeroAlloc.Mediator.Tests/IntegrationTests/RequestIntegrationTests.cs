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
}
