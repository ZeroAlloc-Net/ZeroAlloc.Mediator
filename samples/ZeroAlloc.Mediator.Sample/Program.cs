using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Sample;
using System.Runtime.CompilerServices;

// ============================================================
// Configure handler factories (optional — for DI)
// ============================================================
Mediator.Configure(cfg =>
{
    // Handlers without factories are instantiated with parameterless constructor
    // cfg.SetFactory<PingHandler>(() => new PingHandler(someService));
});

// ============================================================
// Request/Response
// ============================================================
Console.WriteLine("=== Request/Response ===");
var pong = await Mediator.Send(new Ping("Hello ZeroAlloc.Mediator!"), CancellationToken.None).ConfigureAwait(false);
Console.WriteLine(pong);

// ============================================================
// Notification (sequential)
// ============================================================
Console.WriteLine("\n=== Notification (sequential) ===");
await Mediator.Publish(new UserCreated(42, "Alice"), CancellationToken.None).ConfigureAwait(false);

// ============================================================
// Notification (parallel)
// ============================================================
Console.WriteLine("\n=== Notification (parallel via [ParallelNotification]) ===");
await Mediator.Publish(new OrderPlaced(1001, 99.95m), CancellationToken.None).ConfigureAwait(false);

// ============================================================
// Polymorphic Notification
// ============================================================
Console.WriteLine("\n=== Polymorphic Notification ===");
Console.WriteLine("Publishing OrderShipped (implements IOrderNotification):");
await Mediator.Publish(new OrderShipped(1001), CancellationToken.None).ConfigureAwait(false);

Console.WriteLine("Publishing UserCreated (does NOT implement IOrderNotification):");
await Mediator.Publish(new UserCreated(43, "Bob"), CancellationToken.None).ConfigureAwait(false);

// ============================================================
// Streaming
// ============================================================
Console.WriteLine("\n=== Streaming ===");
Console.Write("Counting: ");
await foreach (var n in Mediator.CreateStream(new CountTo(5), CancellationToken.None).ConfigureAwait(false))
{
    Console.Write($"{n} ");
}
Console.WriteLine();

// ============================================================
// Pipeline Behaviors (applied to all Send requests)
// ============================================================
Console.WriteLine("\n=== Pipeline Behaviors ===");
var result = await Mediator.Send(new CreateUser("Charlie"), CancellationToken.None).ConfigureAwait(false);
Console.WriteLine($"Created user ID: {result}");

// ============================================================
// Dependency Injection (IMediator interface)
// ============================================================
Console.WriteLine("\n=== Dependency Injection ===");
var services = new ServiceCollection();
services.AddMediator();
var provider = services.BuildServiceProvider();

var mediator = provider.GetRequiredService<IMediator>();
var diPong = await mediator.Send(new Ping("via DI"), CancellationToken.None).ConfigureAwait(false);
Console.WriteLine(diPong);

await mediator.Publish(new UserCreated(99, "DI User"), CancellationToken.None).ConfigureAwait(false);

Console.Write("DI Stream: ");
await foreach (var n in mediator.CreateStream(new CountTo(3), CancellationToken.None).ConfigureAwait(false))
{
    Console.Write($"{n} ");
}
Console.WriteLine();

// ============================================================
// Types
// ============================================================

namespace ZeroAlloc.Mediator.Sample
{
    using System.Runtime.InteropServices;

    // --- Requests ---
    public readonly record struct Ping(string Message) : IRequest<string>;
    public readonly record struct CreateUser(string Name) : IRequest<int>;

    // --- Notifications ---
    public readonly record struct UserCreated(int Id, string Name) : INotification;

    [ParallelNotification]
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct OrderPlaced(int OrderId, decimal Amount) : INotification;

    // Intermediate notification interface for polymorphic dispatch
    public interface IOrderNotification : INotification { }
    public readonly record struct OrderShipped(int OrderId) : IOrderNotification;

    // --- Stream ---
    public readonly record struct CountTo(int Max) : IStreamRequest<int>;

    // ============================================================
    // Handlers
    // ============================================================

    // --- Request handlers ---
    public class PingHandler : IRequestHandler<Ping, string>
    {
        public ValueTask<string> Handle(Ping request, CancellationToken ct)
            => ValueTask.FromResult($"Pong: {request.Message}");
    }

    public class CreateUserHandler : IRequestHandler<CreateUser, int>
    {
        private static int _nextId = 100;

        public ValueTask<int> Handle(CreateUser request, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref _nextId);
            Console.WriteLine($"  [CreateUserHandler] Creating user '{request.Name}' with ID {id}");
            return ValueTask.FromResult(id);
        }
    }

    // --- Notification handlers ---
    public class UserCreatedLogger : INotificationHandler<UserCreated>
    {
        public ValueTask Handle(UserCreated notification, CancellationToken ct)
        {
            Console.WriteLine($"  [UserCreatedLogger] User created: {notification.Name} (ID: {notification.Id})");
            return ValueTask.CompletedTask;
        }
    }

    public class UserCreatedEmailSender : INotificationHandler<UserCreated>
    {
        public ValueTask Handle(UserCreated notification, CancellationToken ct)
        {
            Console.WriteLine($"  [UserCreatedEmailSender] Welcome email sent to {notification.Name}");
            return ValueTask.CompletedTask;
        }
    }

    public class OrderPlacedAnalytics : INotificationHandler<OrderPlaced>
    {
        public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
        {
            Console.WriteLine($"  [OrderPlacedAnalytics] Order #{notification.OrderId}: ${notification.Amount}");
            return ValueTask.CompletedTask;
        }
    }

    public class OrderPlacedInventory : INotificationHandler<OrderPlaced>
    {
        public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
        {
            Console.WriteLine($"  [OrderPlacedInventory] Reserving stock for order #{notification.OrderId}");
            return ValueTask.CompletedTask;
        }
    }

    public class OrderShippedHandler : INotificationHandler<OrderShipped>
    {
        public ValueTask Handle(OrderShipped notification, CancellationToken ct)
        {
            Console.WriteLine($"  [OrderShippedHandler] Order #{notification.OrderId} shipped");
            return ValueTask.CompletedTask;
        }
    }

    // Polymorphic: handles ALL notifications implementing IOrderNotification
    public class OrderNotificationAuditor : INotificationHandler<IOrderNotification>
    {
        public ValueTask Handle(IOrderNotification notification, CancellationToken ct)
        {
            Console.WriteLine($"  [OrderNotificationAuditor] Auditing order notification: {notification}");
            return ValueTask.CompletedTask;
        }
    }

    // Polymorphic: handles ALL notifications (global logger)
    public class GlobalNotificationLogger : INotificationHandler<INotification>
    {
        public ValueTask Handle(INotification notification, CancellationToken ct)
        {
            Console.WriteLine($"  [GlobalNotificationLogger] {notification.GetType().Name}: {notification}");
            return ValueTask.CompletedTask;
        }
    }

    // --- Stream handler ---
    public class CountToHandler : IStreamRequestHandler<CountTo, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountTo request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= request.Max; i++)
                yield return i;
        }
    }

    // ============================================================
    // Pipeline Behaviors
    // ============================================================

    [PipelineBehavior(Order = 0)]
    public class LoggingBehavior : IPipelineBehavior
    {
        public static ValueTask<TResponse> Handle<TRequest, TResponse>(
            TRequest request, CancellationToken ct,
            Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
            where TRequest : IRequest<TResponse>
        {
            Console.WriteLine($"  [LoggingBehavior] >> {typeof(TRequest).Name}");
            var result = next(request, ct);
            Console.WriteLine($"  [LoggingBehavior] << {typeof(TRequest).Name}");
            return result;
        }
    }

    [PipelineBehavior(Order = 1, AppliesTo = typeof(CreateUser))]
    public class CreateUserValidation : IPipelineBehavior
    {
        public static ValueTask<TResponse> Handle<TRequest, TResponse>(
            TRequest request, CancellationToken ct,
            Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
            where TRequest : IRequest<TResponse>
        {
            Console.WriteLine($"  [CreateUserValidation] Validating {request}");
            return next(request, ct);
        }
    }
}
