using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct IntegrationUserCreated(int Id, string Name) : INotification;

public class IntegrationUserCreatedHandler : INotificationHandler<IntegrationUserCreated>
{
    public static string? LastName { get; set; }

    public ValueTask Handle(IntegrationUserCreated notification, CancellationToken ct)
    {
        LastName = notification.Name;
        IntegrationUserCreatedCounters.HandlerACount++;
        return ValueTask.CompletedTask;
    }
}

public class IntegrationUserCreatedSecondHandler : INotificationHandler<IntegrationUserCreated>
{
    public ValueTask Handle(IntegrationUserCreated notification, CancellationToken ct)
    {
        IntegrationUserCreatedCounters.HandlerBCount++;
        return ValueTask.CompletedTask;
    }
}

internal static class IntegrationUserCreatedCounters
{
    public static int HandlerACount;
    public static int HandlerBCount;

    public static void Reset()
    {
        HandlerACount = 0;
        HandlerBCount = 0;
    }
}

public class NotificationIntegrationTests
{
    [Fact]
    public async Task Publish_DispatchesToHandler()
    {
        IntegrationUserCreatedHandler.LastName = null;

        await Mediator.Publish(new IntegrationUserCreated(42, "Alice"), CancellationToken.None);

        Assert.Equal("Alice", IntegrationUserCreatedHandler.LastName);
    }

    [Fact]
    public async Task Publish_ViaDi_DispatchesToAllRegisteredHandlers()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(IntegrationUserCreatedHandler).Assembly);

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        IntegrationUserCreatedCounters.Reset();
        IntegrationUserCreatedHandler.LastName = null;

        await mediator.Publish(new IntegrationUserCreated(7, "Bob"), CancellationToken.None);

        Assert.Equal(1, IntegrationUserCreatedCounters.HandlerACount);
        Assert.Equal(1, IntegrationUserCreatedCounters.HandlerBCount);
        Assert.Equal("Bob", IntegrationUserCreatedHandler.LastName);
    }
}
