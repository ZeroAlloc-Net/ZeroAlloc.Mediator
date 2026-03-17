---
id: testing
title: Testing
slug: /docs/testing
description: Unit-test request handlers, notifications, and pipeline behaviors.
sidebar_position: 10
---

# Testing

ZeroAlloc.Mediator is designed to be trivially testable. Because every handler is a plain class with a `Handle` method, the most common approach is to instantiate the handler directly and call `Handle` — no mediator, no mocking framework required. Integration tests use the real generated `Mediator` class.

## Testing a Request Handler Directly

The handler is a plain class. Instantiate it, call `Handle`, and assert the result. No mediator involved:

```csharp
public readonly record struct IntegrationPing(string Message) : IRequest<string>;

public class IntegrationPingHandler : IRequestHandler<IntegrationPing, string>
{
    public ValueTask<string> Handle(IntegrationPing request, CancellationToken ct)
        => ValueTask.FromResult($"Pong: {request.Message}");
}

[Fact]
public async Task Handle_ReturnsPongMessage()
{
    var handler = new IntegrationPingHandler();

    var result = await handler.Handle(new IntegrationPing("Hello"), CancellationToken.None);

    Assert.Equal("Pong: Hello", result);
}
```

For handlers with constructor dependencies, pass in test doubles directly:

```csharp
[Fact]
public async Task Handle_SavesProduct_AndReturnsId()
{
    var fakeRepo = new FakeProductRepository();
    var handler = new CreateProductHandler(fakeRepo);

    var result = await handler.Handle(
        new CreateProductCommand("Widget", "WGT-001", 9.99m, 100),
        CancellationToken.None);

    Assert.NotEqual(default, result.Value);
    Assert.Single(fakeRepo.Saved);
}
```

## Testing with the Real Mediator (Integration Tests)

The source generator emits the static `Mediator` class into your test assembly when handlers are declared at the project level. Integration tests call `Mediator.Send` / `Mediator.Publish` / `Mediator.CreateStream` directly — no special setup required.

```csharp
// Declare handler types at the top level of the test project
// (or in a separate project that the test project references)
public readonly record struct IntegrationAdd(int A, int B) : IRequest<int>;

public class IntegrationAddHandler : IRequestHandler<IntegrationAdd, int>
{
    public ValueTask<int> Handle(IntegrationAdd request, CancellationToken ct)
        => ValueTask.FromResult(request.A + request.B);
}

[Fact]
public async Task Send_ReturnsComputedResult()
{
    var result = await Mediator.Send(new IntegrationAdd(3, 4), CancellationToken.None);

    Assert.Equal(7, result);
}
```

For handlers with dependencies, register factories before the test runs:

```csharp
public class RequestIntegrationTests : IDisposable
{
    private readonly FakeOrderRepository _repo;

    public RequestIntegrationTests()
    {
        _repo = new FakeOrderRepository();
        Mediator.Configure(cfg =>
        {
            cfg.SetFactory(() => new PlaceOrderHandler(_repo));
        });
    }

    [Fact]
    public async Task Send_PlaceOrderCommand_PersistsOrder()
    {
        var result = await Mediator.Send(
            new PlaceOrderCommand("cust-1", [new OrderLineItem("SKU-001", 1, 9.99m)]),
            CancellationToken.None);

        Assert.NotEqual(default, result.Value);
        Assert.Single(_repo.Orders);
    }

    public void Dispose()
    {
        // Note: Mediator.Configure with an empty action is a no-op — it does NOT reset factories.
        // To reset a factory between tests, call Configure again with the desired factory,
        // or restructure the test to avoid shared factory state.
        Mediator.Configure(cfg => { });
    }
}
```

## Mocking IMediator in Controller Tests

The generator emits a strongly-typed `IMediator` interface. Use any mocking library to substitute it in unit tests for controllers or services that depend on `IMediator`:

```csharp
[Fact]
public async Task PlaceOrder_Returns201_WithOrderId()
{
    var expectedId = new OrderId(Guid.NewGuid());

    // NSubstitute example
    var mediator = Substitute.For<IMediator>();
    mediator.Send(Arg.Any<PlaceOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(expectedId);

    var controller = new OrdersController(mediator);
    var result = await controller.PlaceOrder(
        new PlaceOrderRequest("cust-1", []),
        CancellationToken.None);

    var created = Assert.IsType<CreatedAtActionResult>(result);
    Assert.Equal(expectedId, created.Value);
}
```

Because `IMediator` contains strongly-typed overloads (one per request/notification/stream type), the mock setup is fully typed — no `object` casting.

## Testing Pipeline Behaviors in Isolation

A pipeline behavior is a static class with a static `Handle` method. Test it by calling the method directly, passing a lambda as the `next` delegate:

```csharp
// Note: in real usage, `: IPipelineBehavior` is required so the generator registers this behavior.
// The tests below call the static method directly, so the interface is not strictly needed for isolation testing.
[PipelineBehavior(Order = 0)]
public static class ValidationBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        if (request is PlaceOrderCommand cmd && cmd.Items.Count == 0)
            throw new ValidationException("Order must have at least one item.");

        return await next(request, ct);
    }
}

[Fact]
public async Task ValidationBehavior_ThrowsWhenNoItems()
{
    var request = new PlaceOrderCommand("cust-1", []);

    await Assert.ThrowsAsync<ValidationException>(() =>
        ValidationBehavior.Handle<PlaceOrderCommand, OrderId>(
            request,
            CancellationToken.None,
            (req, token) => ValueTask.FromResult(new OrderId(Guid.NewGuid()))).AsTask());
}

[Fact]
public async Task ValidationBehavior_CallsNext_WhenValid()
{
    var expectedId = new OrderId(Guid.NewGuid());
    var request = new PlaceOrderCommand("cust-1", [new OrderLineItem("SKU-001", 1, 9.99m)]);
    var nextCalled = false;

    var result = await ValidationBehavior.Handle<PlaceOrderCommand, OrderId>(
        request,
        CancellationToken.None,
        (req, token) =>
        {
            nextCalled = true;
            return ValueTask.FromResult(expectedId);
        });

    Assert.True(nextCalled);
    Assert.Equal(expectedId, result);
}
```

## Testing Notifications

### Verify handler was called

Because notification handlers are plain classes, capture side effects in a field or property:

```csharp
public class TestNotificationHandler : INotificationHandler<UserCreatedEvent>
{
    public string? LastName { get; private set; }

    public ValueTask Handle(UserCreatedEvent notification, CancellationToken ct)
    {
        LastName = notification.Name;
        return ValueTask.CompletedTask;
    }
}

[Fact]
public async Task Handle_SetsLastName()
{
    var handler = new TestNotificationHandler();

    await handler.Handle(new UserCreatedEvent(42, "Alice"), CancellationToken.None);

    Assert.Equal("Alice", handler.LastName);
}
```

### Integration test for Publish dispatch

```csharp
// Handler declared at project level so the generator includes it
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

[Fact]
public async Task Publish_DispatchesToHandler()
{
    IntegrationUserCreatedHandler.LastName = null;

    await Mediator.Publish(new IntegrationUserCreated(42, "Alice"), CancellationToken.None);

    Assert.Equal("Alice", IntegrationUserCreatedHandler.LastName);
}
```

## Testing Streaming

Test stream handlers the same way as request handlers — instantiate and call `Handle` directly, collecting results with `await foreach`:

```csharp
public readonly record struct CountRequest(int Count) : IStreamRequest<int>;

public class CountHandler : IStreamRequestHandler<CountRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        CountRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
    }
}

[Fact]
public async Task StreamHandler_YieldsExpectedValues()
{
    var handler = new CountHandler();
    var results = new List<int>();

    await foreach (var item in handler.Handle(new CountRequest(3), CancellationToken.None))
    {
        results.Add(item);
    }

    Assert.Equal([0, 1, 2], results);
}
```

## Testing Generator Diagnostics

The `GeneratorTestHelper` utility in the test project lets you run the source generator against an arbitrary C# string and inspect the emitted code and diagnostics:

```csharp
// From tests/ZeroAlloc.Mediator.Tests/GeneratorTests/GeneratorTestHelper.cs
var (output, diagnostics) = GeneratorTestHelper.RunGenerator("""
    using ZeroAlloc.Mediator;
    namespace TestApp;
    public readonly record struct Ping : IRequest<string>;
    // No handler — should trigger ZAM001
    """);

var zam001 = diagnostics.FirstOrDefault(d => d.Id == "ZAM001");
Assert.NotNull(zam001);
Assert.Equal(DiagnosticSeverity.Error, zam001.Severity);
```

`GeneratorTestHelper.RunGenerator` creates an in-memory Roslyn compilation, runs the `MediatorGenerator`, and returns the generated source text alongside any diagnostics. This enables tests that verify the generator's behavior (ZAM001–ZAM007 rules, generated method signatures, `MediatorConfig` structure) without requiring a full build.

## No Test Helper Package

ZeroAlloc.Mediator does not ship a dedicated test helper NuGet package. Because handlers are plain classes and `IMediator` is a generated interface, standard mocking libraries (NSubstitute, Moq, FakeItEasy) and xUnit/NUnit/MSTest work without any adapter layer.
