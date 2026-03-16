# Cookbook: Testing Handlers and Behaviors

Handlers are plain C# classes with no framework coupling. The best way to test them is directly — instantiate the handler with fake dependencies, call `Handle(request, ct)`, and assert the result. This is faster, simpler, and more focused than going through `Mediator.Send`.

## The Core Principle

> **Test handlers directly, not through `Mediator.Send`.**

The mediator dispatch is generated code — it's tested by the library itself. Your business logic lives in the handler. That's what needs tests.

Going through `Mediator.Send` in unit tests adds: factory resolution, pipeline behaviors (you may not want), and generated code you don't own. Just call the handler.

## Fake Dependencies

Before showing tests, establish the fake pattern:

```csharp
// In-memory fake implementing the repository interface
public class FakeProductRepository : IProductRepository
{
    public List<Product> Saved { get; } = [];

    public Task SaveAsync(Guid id, string name, string sku, decimal price, int stock, CancellationToken ct)
    {
        Saved.Add(new Product { Id = id, Name = name, Sku = sku, Price = price, StockLevel = stock });
        return Task.CompletedTask;
    }

    public Task<Product?> FindAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Saved.Any(p => p.Id == id));

    public Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var p = Saved.First(p => p.Id == id);
        p.IsArchived = true;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<Product> StreamByDateRangeAsync(
        DateTimeOffset from, DateTimeOffset to, string? customerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var p in Saved.Where(p => !p.IsArchived))
            yield return p;
    }
}
```

## Unit Testing a Request Handler

```csharp
// xUnit
public class CreateProductHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_SavesProductAndReturnsId()
    {
        // Arrange
        var repo = new FakeProductRepository();
        var handler = new CreateProductHandler(repo);
        var command = new CreateProductCommand("Widget Pro", "WGT-001", 49.99m, 100);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.NotEqual(Guid.Empty, result.Value);
        Assert.Single(repo.Saved);
        Assert.Equal("Widget Pro", repo.Saved[0].Name);
        Assert.Equal("WGT-001", repo.Saved[0].Sku);
        Assert.Equal(49.99m, repo.Saved[0].Price);
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        var handler = new CreateProductHandler(new FakeProductRepository());
        var command = new CreateProductCommand("", "WGT-001", 49.99m, 100);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_NegativePrice_ThrowsArgumentOutOfRangeException()
    {
        var handler = new CreateProductHandler(new FakeProductRepository());
        var command = new CreateProductCommand("Widget", "WGT-001", -1m, 100);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(command, CancellationToken.None).AsTask());
    }
}
```

## Unit Testing a Query Handler

```csharp
public class GetProductHandlerTests
{
    [Fact]
    public async Task Handle_ExistingProduct_ReturnsDtoWithCorrectData()
    {
        // Arrange
        var repo = new FakeProductRepository();
        var productId = Guid.NewGuid();
        repo.Saved.Add(new Product
        {
            Id = productId, Name = "Widget", Sku = "WGT-001",
            Price = 9.99m, StockLevel = 50, IsArchived = false
        });

        var handler = new GetProductHandler(repo);

        // Act
        var dto = await handler.Handle(new GetProductQuery(productId), CancellationToken.None);

        // Assert
        Assert.Equal(productId, dto.Id);
        Assert.Equal("Widget", dto.Name);
        Assert.Equal(9.99m, dto.Price);
    }

    [Fact]
    public async Task Handle_MissingProduct_ThrowsProductNotFoundException()
    {
        var handler = new GetProductHandler(new FakeProductRepository());

        await Assert.ThrowsAsync<ProductNotFoundException>(() =>
            handler.Handle(new GetProductQuery(Guid.NewGuid()), CancellationToken.None).AsTask());
    }
}
```

## Unit Testing a Notification Handler

```csharp
public class SendShipmentEmailHandlerTests
{
    [Fact]
    public async Task Handle_OrderShippedEvent_SendsEmailWithTrackingNumber()
    {
        // Arrange
        var emailService = new FakeEmailService();
        var handler = new SendShipmentEmailHandler(emailService);
        var evt = new OrderShippedEvent(
            Guid.NewGuid(), "1Z999AA10123456784", "UPS", DateTimeOffset.UtcNow);

        // Act
        await handler.Handle(evt, CancellationToken.None);

        // Assert
        Assert.Single(emailService.SentEmails);
        Assert.Contains("1Z999AA10123456784", emailService.SentEmails[0].Body);
    }
}

public class FakeEmailService : IEmailService
{
    public record SentEmail(Guid OrderId, string Body);
    public List<SentEmail> SentEmails { get; } = [];

    public Task SendShipmentConfirmationAsync(Guid orderId, string tracking, CancellationToken ct)
    {
        SentEmails.Add(new SentEmail(orderId, $"Your order {orderId} shipped. Tracking: {tracking}"));
        return Task.CompletedTask;
    }
    // ... other methods return Task.CompletedTask
}
```

## Unit Testing a Pipeline Behavior

Behaviors are static — test the static method directly by providing a `next` delegate:

```csharp
public class LoggingBehaviorTests
{
    [Fact]
    public async Task Handle_SuccessfulRequest_CallsNextAndReturnsResult()
    {
        // Arrange
        var request = new CreateProductCommand("Widget", "WGT-001", 9.99m, 50);
        var expectedId = new ProductId(Guid.NewGuid());
        var nextCalled = false;

        Func<CreateProductCommand, CancellationToken, ValueTask<ProductId>> next =
            (_, _) => { nextCalled = true; return ValueTask.FromResult(expectedId); };

        // Act
        var result = await LoggingBehavior.Handle(request, CancellationToken.None, next);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(expectedId, result);
    }

    [Fact]
    public async Task Handle_HandlerThrows_ExceptionPropagates()
    {
        var request = new CreateProductCommand("Widget", "WGT-001", 9.99m, 50);

        Func<CreateProductCommand, CancellationToken, ValueTask<ProductId>> next =
            (_, _) => throw new InvalidOperationException("handler failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LoggingBehavior.Handle(request, CancellationToken.None, next).AsTask());
    }
}
```

## Integration Testing with Mediator.Configure

When you need to test the full dispatch path (request → behaviors → handler → response):

```csharp
public class PlaceOrderIntegrationTests : IDisposable
{
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeInventoryService _inventory = new();

    public PlaceOrderIntegrationTests()
    {
        // Wire up real handler with fake dependencies
        Mediator.Configure(cfg =>
            cfg.SetFactory(() => new PlaceOrderHandler(_orders)));
    }

    [Fact]
    public async Task PlaceOrder_ValidCommand_CreatesOrderAndPublishesEvent()
    {
        var command = new PlaceOrderCommand("customer-123", [
            new OrderLineItem("SKU-001", 2, 29.99m)
        ]);

        var orderId = await Mediator.Send(command);

        Assert.NotEqual(Guid.Empty, orderId.Value);
        Assert.Single(_orders.Created);
        Assert.Equal("customer-123", _orders.Created[0].CustomerId);
    }

    public void Dispose()
    {
        // Reset factories between tests to avoid state leaking between test classes
        Mediator.Configure(cfg => cfg.SetFactory<PlaceOrderHandler>(null!));
    }
}
```

## Testing Streaming Handlers

```csharp
public class ExportOrdersCsvHandlerTests
{
    [Fact]
    public async Task Handle_OrdersInDateRange_YieldsMatchingRows()
    {
        // Arrange
        var repo = new FakeProductRepository();
        var inRange = new Product
        {
            Id = Guid.NewGuid(), CustomerId = "cust-1",
            PlacedAt = DateTimeOffset.UtcNow.AddDays(-3),
            TotalAmount = 99.99m, Status = OrderStatus.Shipped
        };
        var outOfRange = new Product
        {
            Id = Guid.NewGuid(), CustomerId = "cust-2",
            PlacedAt = DateTimeOffset.UtcNow.AddDays(-10), // outside window
            TotalAmount = 49.99m, Status = OrderStatus.Pending
        };
        repo.Orders.AddRange([inRange, outOfRange]);

        var handler = new ExportOrdersCsvHandler(repo);
        var query = new ExportOrdersCsvQuery(
            DateTimeOffset.UtcNow.AddDays(-5),
            DateTimeOffset.UtcNow);

        // Act
        var rows = new List<OrderCsvRow>();
        await foreach (var row in handler.Handle(query, CancellationToken.None))
            rows.Add(row);

        // Assert
        Assert.Single(rows);
        Assert.Equal(inRange.Id.ToString("D"), rows[0].OrderId);
    }

    [Fact]
    public async Task Handle_CancellationRequested_StopsIteration()
    {
        var repo = new FakeProductRepository();
        // Add 100 orders
        for (var i = 0; i < 100; i++)
            repo.Orders.Add(new Product { Id = Guid.NewGuid(), PlacedAt = DateTimeOffset.UtcNow });

        var handler = new ExportOrdersCsvHandler(repo);
        var query = new ExportOrdersCsvQuery(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);

        using var cts = new CancellationTokenSource();
        var rows = new List<OrderCsvRow>();

        await foreach (var row in handler.Handle(query, cts.Token))
        {
            rows.Add(row);
            if (rows.Count == 5) cts.Cancel(); // Cancel after 5 rows
        }

        // Should have stopped near 5, not processed all 100
        Assert.True(rows.Count <= 10); // allow a few extra due to buffering
    }
}
```

## Mocking IMediator for Controller/Endpoint Tests

When testing code that injects `IMediator`, mock it:

```csharp
// Using NSubstitute
public class ProductsEndpointTests
{
    [Fact]
    public async Task PostProduct_ValidRequest_Returns201WithProductId()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        var expectedId = new ProductId(Guid.NewGuid());
        mediator.Send(Arg.Any<CreateProductCommand>(), Arg.Any<CancellationToken>())
                .Returns(expectedId);

        // Act — call the endpoint handler function directly
        var result = await ProductEndpoints.CreateProduct(
            new CreateProductRequest("Widget", "WGT-001", 9.99m, 100),
            mediator,
            CancellationToken.None);

        // Assert
        var created = Assert.IsType<Created<ProductId>>(result);
        Assert.Equal(expectedId.Value, created.Value!.Value);
    }
}
```

## Related

- [Requests & Handlers](../02-requests.md)
- [Pipeline Behaviors](../05-pipeline-behaviors.md)
- [Dependency Injection](../06-dependency-injection.md)
