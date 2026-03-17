---
id: advanced
title: Advanced Patterns
slug: /docs/advanced
description: Error handling, cancellation, scoped behaviors, and combining Mediator features.
sidebar_position: 9
---

# Advanced Patterns

This page covers patterns that go beyond the basic request/handler/publish cycle: error propagation, cancellation flow, combining features, and accessing scoped DI services from pipeline behaviors.

## Error Handling

### Exception propagation through the mediator

ZeroAlloc.Mediator does not catch or wrap exceptions. If a handler throws, the exception propagates directly to the `await` call site — no wrapping in `AggregateException` (except for parallel notifications via `Task.WhenAll`), no swallowing, no retry.

```csharp
// Handler that throws
public class GetProductHandler : IRequestHandler<GetProductQuery, ProductDto>
{
    public async ValueTask<ProductDto> Handle(GetProductQuery query, CancellationToken ct)
    {
        var product = await _repo.FindAsync(query.ProductId, ct)
            ?? throw new ProductNotFoundException(query.ProductId); // propagates directly
        return Map(product);
    }
}

// Call site — exception propagates as-is
try
{
    var dto = await Mediator.Send(new GetProductQuery(id), ct);
}
catch (ProductNotFoundException ex)
{
    return Results.NotFound(ex.Message);
}
```

### Handling exceptions at the pipeline level

The recommended pattern for cross-cutting exception handling is a pipeline behavior at `Order = 0` (outermost), so it wraps the entire dispatch chain:

```csharp
[PipelineBehavior(Order = 0)]
public static class ExceptionHandlingBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            return await next(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Let cancellation propagate naturally — do not convert to another exception
            throw;
        }
        catch (DomainException)
        {
            // Domain exceptions are intentional — let them propagate
            throw;
        }
        catch (Exception ex)
        {
            // Log unexpected exceptions before rethrowing
            Console.Error.WriteLine($"Unhandled exception in {typeof(TRequest).Name}: {ex}");
            throw;
        }
    }
}
```

### Parallel notifications and AggregateException

When a notification is marked `[ParallelNotification]`, the generated `Publish` method uses `Task.WhenAll`. If multiple handlers throw, all exceptions are collected into a single `AggregateException`:

```csharp
[ParallelNotification]
public readonly record struct PaymentReceivedEvent(Guid OrderId, decimal Amount) : INotification;

// At the call site, handle AggregateException
try
{
    await Mediator.Publish(new PaymentReceivedEvent(orderId, amount), ct);
}
catch (AggregateException ex)
{
    foreach (var inner in ex.InnerExceptions)
        Console.Error.WriteLine($"Handler failed: {inner.Message}");
}
```

Sequential notifications (the default) propagate the first exception and stop dispatching to remaining handlers. This is a deliberate design decision: if handler A throws, handlers B and C do not run.

## Cancellation

### CancellationToken flow

Every generated overload — `Send`, `Publish`, and `CreateStream` — accepts a `CancellationToken` with a default value of `default`. The token is passed directly to the handler's `Handle(request, ct)` method without modification:

```csharp
// Generated code (conceptual)
public static ValueTask<TResponse> Send(TRequest request, CancellationToken ct = default)
    => handler.Handle(request, ct);
```

This means:
- You are responsible for checking `ct` inside your handler.
- The mediator itself does not call `ct.ThrowIfCancellationRequested()` before dispatch.
- Cancellation during `await next(request, ct)` inside a pipeline behavior propagates as `OperationCanceledException` to the outer behavior and then to the call site.

### Cancellation in request handlers

```csharp
public class ExportReportHandler : IRequestHandler<ExportReportCommand, ReportId>
{
    public async ValueTask<ReportId> Handle(ExportReportCommand cmd, CancellationToken ct)
    {
        // Pass ct to every async call — don't swallow it
        var data = await _repo.GetDataAsync(cmd.Filters, ct);
        ct.ThrowIfCancellationRequested(); // explicit check in CPU-bound loops
        var reportId = await _storage.SaveAsync(data, ct);
        return reportId;
    }
}
```

### Cancellation in stream handlers

For `IStreamRequestHandler`, the `[EnumeratorCancellation]` attribute is required to propagate the token passed to `Mediator.CreateStream` into the async iterator:

```csharp
public class ExportOrdersHandler : IStreamRequestHandler<ExportOrdersQuery, OrderRow>
{
    public async IAsyncEnumerable<OrderRow> Handle(
        ExportOrdersQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in _repo.StreamAsync(query.From, query.To, ct))
        {
            ct.ThrowIfCancellationRequested(); // defensive check in tight loops
            yield return Map(order);
        }
    }
}
```

Without `[EnumeratorCancellation]`, the token passed to `CreateStream` is not forwarded into the iterator — the stream would run to completion even if the caller cancels.

### ASP.NET Core request cancellation

ASP.NET Core automatically wires `HttpContext.RequestAborted` into the `CancellationToken` parameter of minimal API endpoints and controller actions. Pass it through to `Mediator.Send` or `CreateStream`:

```csharp
app.MapGet("/orders/export", (IMediator mediator, CancellationToken ct) =>
    mediator.CreateStream(new ExportOrdersQuery(), ct));
```

## Combining Features

### Pipeline behavior + streaming

Pipeline behaviors only wrap `Send` calls (request/response dispatch). They do **not** wrap `CreateStream`. If you need middleware around a stream, apply the logic inside the stream handler itself or wrap the call at the application layer:

```csharp
// Logging around a stream at the call site
var query = new ExportOrdersQuery(from, to);
Console.WriteLine($"[START] ExportOrdersQuery");
var sw = Stopwatch.StartNew();
int count = 0;

await foreach (var row in Mediator.CreateStream(query, ct))
{
    count++;
    yield return row;
}

Console.WriteLine($"[END] ExportOrdersQuery — {count} rows in {sw.ElapsedMilliseconds}ms");
```

### Notification + pipeline behavior

Pipeline behaviors do **not** wrap `Publish` calls. Notifications are dispatched directly to handlers without going through the behavior chain. If you need cross-cutting behavior on notification dispatch (e.g., logging every publication), use a polymorphic base handler:

```csharp
// Runs for EVERY notification — acts as a global notification middleware
public class NotificationAuditHandler : INotificationHandler<INotification>
{
    public ValueTask Handle(INotification notification, CancellationToken ct)
    {
        Console.WriteLine($"[PUBLISH] {notification.GetType().Name} at {DateTimeOffset.UtcNow:O}");
        return ValueTask.CompletedTask;
    }
}
```

The generator includes this handler in the dispatch chain for every concrete notification type at compile time.

### Multiple behaviors with AppliesTo

You can mix global behaviors (no `AppliesTo`) with request-specific behaviors (`AppliesTo = typeof(T)`). The generator applies both to the targeted request type, ordering them by `Order` value:

```csharp
[PipelineBehavior(Order = 0)]          // applies to ALL requests
public static class LoggingBehavior { ... }

[PipelineBehavior(Order = 5, AppliesTo = typeof(PlaceOrderCommand))]
public static class StockValidationBehavior { ... }  // only PlaceOrderCommand

[PipelineBehavior(Order = 10)]         // applies to ALL requests
public static class PerformanceMonitorBehavior { ... }
```

For `PlaceOrderCommand` the chain is: `LoggingBehavior` → `StockValidationBehavior` → `PerformanceMonitorBehavior` → handler.

For all other requests: `LoggingBehavior` → `PerformanceMonitorBehavior` → handler.

## Scoped Behaviors

### The problem: static behaviors have no instance state

Pipeline behaviors require a `static` `Handle` method — the generator emits them as static method calls, not instantiated objects. The class itself does not need to be `static`, but because `Handle` is static, you cannot inject services via a constructor. For stateless cross-cutting concerns (logging via `Console`, performance counters, lightweight validation) this is fine.

For behaviors that genuinely need a scoped service (e.g., `DbContext`, `ICurrentUserService`, or a per-request audit log), use an ambient context pattern:

### Pattern 1 — HttpContext / IHttpContextAccessor

In ASP.NET Core, resolve scoped services via `IHttpContextAccessor` stored as a static or thread-local ambient:

```csharp
// Register in Program.cs
builder.Services.AddHttpContextAccessor();

// In a behavior that needs the current user
[PipelineBehavior(Order = 5)]
public static class CurrentUserBehavior : IPipelineBehavior
{
    // Set once at startup via Mediator.Configure or DI wiring
    internal static IHttpContextAccessor? HttpContextAccessor;

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var user = HttpContextAccessor?.HttpContext?.User;
        // Use user...
        return await next(request, ct);
    }
}

// Wire up in Program.cs after building the service provider
var app = builder.Build();
CurrentUserBehavior.HttpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
```

### Pattern 2 — AsyncLocal\<T\>

For non-ASP.NET scenarios, use `AsyncLocal<T>` to flow a scoped value through the async call chain:

```csharp
[PipelineBehavior(Order = 0)]
public static class TenantBehavior : IPipelineBehavior
{
    private static readonly AsyncLocal<string?> _tenantId = new();

    public static string? CurrentTenantId => _tenantId.Value;

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        // Populate from the request if it carries a tenant ID
        if (request is ITenantRequest tenantRequest)
            _tenantId.Value = tenantRequest.TenantId;

        try
        {
            return await next(request, ct);
        }
        finally
        {
            _tenantId.Value = null;
        }
    }
}
```

`AsyncLocal<T>` values flow correctly through `await` boundaries. The `finally` block ensures the value is cleared after the dispatch completes, preventing leakage between requests.

## Handler Visibility

The source generator only discovers handler classes that are `public` or `internal` (accessible within the compilation). Handlers that are `private` or nested inside another type are ignored. If a handler does not appear in the generated `Mediator` class, check that its declaration is accessible.

## Conditional Dispatch

The generated mediator has no built-in conditional dispatch. If you need to route to different handlers based on runtime state, implement the condition inside a single handler:

```csharp
public class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, OrderId>
{
    public ValueTask<OrderId> Handle(PlaceOrderCommand cmd, CancellationToken ct)
    {
        return cmd.IsExpressOrder
            ? HandleExpress(cmd, ct)
            : HandleStandard(cmd, ct);
    }

    private ValueTask<OrderId> HandleExpress(PlaceOrderCommand cmd, CancellationToken ct) { ... }
    private ValueTask<OrderId> HandleStandard(PlaceOrderCommand cmd, CancellationToken ct) { ... }
}
```

Alternatively, use a pipeline behavior with `AppliesTo` to perform pre-dispatch validation and abort early by throwing rather than routing to a different handler.
