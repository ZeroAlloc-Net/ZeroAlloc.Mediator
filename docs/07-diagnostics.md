# Compiler Diagnostics

ZeroAlloc.Mediator validates your mediator setup at compile time using a Roslyn analyzer. Misconfigurations appear as build errors or warnings in your IDE and on `dotnet build` — you never discover them at runtime in production.

## Diagnostic Reference Table

| Code | Severity | Title | When it triggers |
|------|----------|-------|------------------|
| ZAM001 | Error | No handler for request | A type implements `IRequest<T>` but has no matching `IRequestHandler` in the project |
| ZAM002 | Error | Multiple handlers for request | More than one `IRequestHandler<TRequest, TResponse>` for the same request type |
| ZAM003 | Warning | Request type is a class | A request type is a `class` instead of `readonly record struct` |
| ZAM004 | Error | Invalid handler signature | Handler method doesn't match the expected interface signature (compiler-enforced) |
| ZAM005 | Error | Pipeline behavior missing Handle method | A class with `[PipelineBehavior]` has no static `Handle<TRequest,TResponse>` method |
| ZAM006 | Warning | Duplicate pipeline behavior Order | Two behaviors have the same `Order` value |
| ZAM007 | Error | Stream handler wrong return type | Stream handler method doesn't return `IAsyncEnumerable<TResponse>` |

## ZAM001 — No Handler for Request

**What it means:** The generator found a type that implements `IRequest<TResponse>` but couldn't find any class implementing `IRequestHandler<TRequest, TResponse>` for it.

**Example that triggers it:**
```csharp
public readonly record struct GetProductQuery(Guid ProductId) : IRequest<ProductDto>;
// No GetProductHandler exists anywhere in the project
```

**Error message:** `ZAM001: No handler found for request type 'GetProductQuery'`

**Fix:** Add the handler:
```csharp
public class GetProductHandler : IRequestHandler<GetProductQuery, ProductDto>
{
    public async ValueTask<ProductDto> Handle(GetProductQuery query, CancellationToken ct)
    {
        // implementation
    }
}
```

**Common traps:**
- The handler is in a separate assembly that isn't referenced by the project containing the request
- The handler class is `internal` to a namespace but the request is in a different project — check visibility
- You renamed the request type but forgot to update the handler's generic parameter

## ZAM002 — Multiple Handlers for Request

**What it means:** Two or more classes both implement `IRequestHandler<TRequest, TResponse>` for the same request type. The generator can't decide which to use.

**Example:**
```csharp
public class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, OrderId> { ... }
public class LegacyPlaceOrderHandler : IRequestHandler<PlaceOrderCommand, OrderId> { ... }  // ❌
```

**Fix:** Remove or rename the duplicate. If you're migrating from one implementation to another, delete the old class before building.

## ZAM003 — Request Type Is a Class

**What it means:** A request type uses `class` instead of `readonly record struct`. This is a warning (not an error) because it compiles fine, but it causes heap allocation on every dispatch.

**Example:**
```csharp
// ❌ Triggers ZAM003
public class PlaceOrderCommand : IRequest<OrderId>
{
    public string CustomerId { get; set; }
    public List<OrderLineItem> Items { get; set; }
}

// ✅ Correct — zero allocation
public readonly record struct PlaceOrderCommand(
    string CustomerId,
    IReadOnlyList<OrderLineItem> Items
) : IRequest<OrderId>;
```

**Note:** If your request genuinely needs reference semantics (e.g., it contains mutable collections that must be shared), a class is acceptable and you can suppress the warning. But for most cases, `readonly record struct` is the right choice.

## ZAM005 — Pipeline Behavior Missing Handle Method

**What it means:** A class marked `[PipelineBehavior]` doesn't have a `static Handle<TRequest, TResponse>` method with the correct signature.

**Example:**
```csharp
// ❌ Triggers ZAM005 — no Handle method
[PipelineBehavior(Order = 0)]
public static class LoggingBehavior
{
    public static void Log(string message) => Console.WriteLine(message); // wrong method
}
```

**Fix:** Add the required static method:
```csharp
[PipelineBehavior(Order = 0)]
public static class LoggingBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    {
        Console.WriteLine($"[START] {typeof(TRequest).Name}");
        var result = await next(request, ct);
        Console.WriteLine($"[END] {typeof(TRequest).Name}");
        return result;
    }
}
```

The method signature must match exactly:
- Generic: `Handle<TRequest, TResponse>`
- Parameters: `(TRequest, CancellationToken, Func<TRequest, CancellationToken, ValueTask<TResponse>>)`
- Return: `ValueTask<TResponse>`
- Access: `public static`

## ZAM006 — Duplicate Pipeline Behavior Order

**What it means:** Two behaviors share the same `Order` value. The generator emits them in source-order, but that's an implementation detail — don't rely on it.

**Example:**
```csharp
// ❌ Both Order=10 — ZAM006 warning
[PipelineBehavior(Order = 10)]
public static class ValidationBehavior { ... }

[PipelineBehavior(Order = 10)]
public static class CachingBehavior { ... }
```

**Fix:** Use unique values:
```csharp
[PipelineBehavior(Order = 10)]
public static class ValidationBehavior { ... }

[PipelineBehavior(Order = 20)]
public static class CachingBehavior { ... }
```

**Convention:** Use multiples of 10 (0, 10, 20, 30...) so you can insert behaviors between existing ones without renumbering.

## ZAM007 — Stream Handler Wrong Return Type

**What it means:** A class implements `IStreamRequestHandler<TRequest, TResponse>` but the `Handle` method doesn't return `IAsyncEnumerable<TResponse>`.

**Example:**
```csharp
// ❌ Returns Task<IEnumerable<T>> — triggers ZAM007
public class ExportOrdersHandler : IStreamRequestHandler<ExportOrdersQuery, OrderExportRow>
{
    public async Task<IEnumerable<OrderExportRow>> Handle(
        ExportOrdersQuery query, CancellationToken ct)
    {
        return await _repo.GetAllAsync(ct);
    }
}
```

**Fix:** Return `IAsyncEnumerable<TResponse>` with `yield return`:
```csharp
public class ExportOrdersHandler : IStreamRequestHandler<ExportOrdersQuery, OrderExportRow>
{
    public async IAsyncEnumerable<OrderExportRow> Handle(
        ExportOrdersQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in _repo.StreamAsync(ct))
            yield return Map(order);
    }
}
```

## Suppressing Warnings

If you intentionally use a class request type (ZAM003) or have duplicate Order values (ZAM006) for a valid reason, suppress with `#pragma`:

```csharp
#pragma warning disable ZAM003
public class MySpecialRequest : IRequest<MyResponse> { ... }
#pragma warning restore ZAM003
```

Or in your `.csproj` for project-wide suppression:
```xml
<PropertyGroup>
    <NoWarn>$(NoWarn);ZAM003</NoWarn>
</PropertyGroup>
```
