using System.Threading;
using System.Threading.Tasks;

namespace ZMediator.Tests.IntegrationTests;

public readonly record struct IntegrationUserCreated(int Id, string Name) : INotification;

public class IntegrationUserCreatedHandler : INotificationHandler<IntegrationUserCreated>
{
    public static string? LastName { get; set; }

    public ValueTask Handle(IntegrationUserCreated notification, CancellationToken ct)
    {
        LastName = notification.Name;
        return ValueTask.CompletedTask;
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
}
