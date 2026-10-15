using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// One class that implements a handler interface more than once is dispatched for each of them,
// by the static Mediator and by IMediator alike (#334).
public readonly record struct MultiOrderPlaced(int Id) : INotification;

public readonly record struct MultiOrderShipped(int Id) : INotification;

public interface IMultiOrderEvent : INotification
{
    int Id { get; }
}

public readonly record struct MultiOrderCancelled(int Id) : IMultiOrderEvent;

public readonly record struct MultiGetName(int Id) : IRequest<string>;

public readonly record struct MultiGetAge(int Id) : IRequest<int>;

public readonly record struct MultiWhoHandled : IRequest<object>;

public readonly record struct MultiWhoHandledToo : IRequest<object>;

// Records which overload ran. A static counter keeps the handler stateless, so the static
// Mediator shares one instance of it.
public sealed class MultiNotificationHandler :
    INotificationHandler<MultiOrderPlaced>,
    INotificationHandler<MultiOrderShipped>,
    INotificationHandler<MultiOrderCancelled>,
    INotificationHandler<IMultiOrderEvent>
{
    public ValueTask Handle(MultiOrderPlaced notification, CancellationToken ct)
    {
        MultiHandlerLog.Placed++;
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(MultiOrderShipped notification, CancellationToken ct)
    {
        MultiHandlerLog.Shipped++;
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(MultiOrderCancelled notification, CancellationToken ct)
    {
        MultiHandlerLog.Cancelled++;
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(IMultiOrderEvent notification, CancellationToken ct)
    {
        MultiHandlerLog.AnyOrderEvent++;
        return ValueTask.CompletedTask;
    }
}

public sealed class MultiRequestHandler :
    IRequestHandler<MultiGetName, string>,
    IRequestHandler<MultiGetAge, int>,
    IRequestHandler<MultiWhoHandled, object>,
    IRequestHandler<MultiWhoHandledToo, object>
{
    public ValueTask<string> Handle(MultiGetName request, CancellationToken ct) => ValueTask.FromResult("name-" + request.Id);

    public ValueTask<int> Handle(MultiGetAge request, CancellationToken ct) => ValueTask.FromResult(request.Id * 10);

    public ValueTask<object> Handle(MultiWhoHandled request, CancellationToken ct) => ValueTask.FromResult<object>(this);

    public ValueTask<object> Handle(MultiWhoHandledToo request, CancellationToken ct) => ValueTask.FromResult<object>(this);
}

internal static class MultiHandlerLog
{
    public static int Placed;
    public static int Shipped;
    public static int Cancelled;
    public static int AnyOrderEvent;

    public static void Reset()
    {
        Placed = 0;
        Shipped = 0;
        Cancelled = 0;
        AnyOrderEvent = 0;
    }
}

public sealed class MultiInterfaceHandlerIntegrationTests
{
    [Fact]
    public async Task StaticMediator_PublishesEachNotification_ToTheOneClass()
    {
        MultiHandlerLog.Reset();

        await Mediator.Publish(new MultiOrderPlaced(1), CancellationToken.None);
        await Mediator.Publish(new MultiOrderShipped(1), CancellationToken.None);

        Assert.Equal(1, MultiHandlerLog.Placed);
        Assert.Equal(1, MultiHandlerLog.Shipped);
    }

    [Fact]
    public async Task DiMediator_PublishesEachNotification_ToTheOneClass()
    {
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        MultiHandlerLog.Reset();

        await mediator.Publish(new MultiOrderPlaced(1), CancellationToken.None);
        await mediator.Publish(new MultiOrderShipped(1), CancellationToken.None);

        Assert.Equal(1, MultiHandlerLog.Placed);
        Assert.Equal(1, MultiHandlerLog.Shipped);
    }

    // The class handles the notification and its base interface, so it runs once per interface,
    // each time through its own overload.
    [Fact]
    public async Task StaticMediator_CallsTheConcreteAndTheBaseOverload_OnceEach()
    {
        MultiHandlerLog.Reset();

        await Mediator.Publish(new MultiOrderCancelled(1), CancellationToken.None);

        Assert.Equal(1, MultiHandlerLog.Cancelled);
        Assert.Equal(1, MultiHandlerLog.AnyOrderEvent);
    }

    [Fact]
    public async Task DiMediator_CallsTheConcreteAndTheBaseOverload_OnceEach()
    {
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();
        MultiHandlerLog.Reset();

        await mediator.Publish(new MultiOrderCancelled(1), CancellationToken.None);

        Assert.Equal(1, MultiHandlerLog.Cancelled);
        Assert.Equal(1, MultiHandlerLog.AnyOrderEvent);
    }

    [Fact]
    public async Task StaticMediator_SendsEachRequest_ToTheOneClass()
    {
        Assert.Equal("name-3", await Mediator.Send(new MultiGetName(3), CancellationToken.None));
        Assert.Equal(30, await Mediator.Send(new MultiGetAge(3), CancellationToken.None));
    }

    [Fact]
    public async Task DiMediator_SendsEachRequest_ToTheOneClass()
    {
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        Assert.Equal("name-3", await mediator.Send(new MultiGetName(3), CancellationToken.None));
        Assert.Equal(30, await mediator.Send(new MultiGetAge(3), CancellationToken.None));
    }

    // The static Mediator keeps one cached instance per handler class, not one per interface.
    [Fact]
    public async Task StaticMediator_SharesOneCachedInstance_AcrossTheClassInterfaces()
    {
        var first = await Mediator.Send(new MultiWhoHandled(), CancellationToken.None);
        var second = await Mediator.Send(new MultiWhoHandledToo(), CancellationToken.None);

        Assert.IsType<MultiRequestHandler>(first);
        Assert.Same(first, second);
    }

    // AddMediator registers the class once, so its lifetime applies to every interface: a
    // singleton serves every request with the same instance.
    [Fact]
    public async Task DiMediator_UsesTheOneRegistration_ForEveryInterface()
    {
        var services = new ServiceCollection();
        services.AddMediator(ServiceLifetime.Singleton);
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var first = await mediator.Send(new MultiWhoHandled(), CancellationToken.None);
        var second = await mediator.Send(new MultiWhoHandledToo(), CancellationToken.None);

        Assert.Same(sp.GetRequiredService<MultiRequestHandler>(), first);
        Assert.Same(first, second);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        return services.BuildServiceProvider();
    }
}
