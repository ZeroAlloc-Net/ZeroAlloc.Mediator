namespace ZMediator.Tests;

public class NotificationInterfaceTests
{
    private readonly record struct TestNotification(string Message) : INotification;

    private class TestHandler : INotificationHandler<TestNotification>
    {
        public string? ReceivedMessage { get; private set; }

        public ValueTask Handle(TestNotification notification, CancellationToken ct)
        {
            ReceivedMessage = notification.Message;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task NotificationHandler_CanHandleNotification()
    {
        var handler = new TestHandler();
        await handler.Handle(new TestNotification("hello"), CancellationToken.None);
        Assert.Equal("hello", handler.ReceivedMessage);
    }

    [Fact]
    public void ParallelNotificationAttribute_CanBeApplied()
    {
        var attr = typeof(ParallelTestNotification)
            .GetCustomAttributes(typeof(ParallelNotificationAttribute), false);
        Assert.Single(attr);
    }

    [ParallelNotification]
    private readonly record struct ParallelTestNotification : INotification;
}
