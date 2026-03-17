# ZeroAlloc.Mediator

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mediator.svg)](https://www.nuget.org/packages/ZeroAlloc.Mediator)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml)

A zero-allocation, Native AOT-compatible mediator library for .NET 8 and .NET 10. Uses a Roslyn incremental source generator to wire all dispatch at compile time — no reflection, no dictionaries, no virtual dispatch, no delegate allocation per request.

## Features

- **Request/Response** — strongly-typed `Send` overloads per request type
- **Notifications** — sequential or parallel (`[ParallelNotification]`) dispatch
- **Streaming** — `IAsyncEnumerable<T>` via `CreateStream`
- **Pipeline Behaviors** — compile-time inlined middleware chain (logging, validation, etc.)
- **Polymorphic Notifications** — base interface handlers are automatically included in concrete notification dispatch
- **Analyzer Diagnostics** — missing handlers, duplicates, and misconfigurations are build errors/warnings
- **Zero Allocation** — `ValueTask`, `readonly record struct`, static dispatch, no closures
- **Native AOT Compatible** — no reflection at runtime; all dispatch is resolved at compile time by the source generator

## Quick Start

Add the NuGet packages:

```xml
<PackageReference Include="ZeroAlloc.Mediator" Version="1.1.1" />
<PackageReference Include="ZeroAlloc.Mediator.Generator" Version="1.1.1" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

### Request/Response

```csharp
public readonly record struct Ping(string Message) : IRequest<string>;

public class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken ct)
        => ValueTask.FromResult($"Pong: {request.Message}");
}

// Usage
var result = await Mediator.Send(new Ping("Hello"), ct);
```

### Notifications

```csharp
public readonly record struct UserCreated(int Id, string Name) : INotification;

public class UserCreatedLogger : INotificationHandler<UserCreated>
{
    public ValueTask Handle(UserCreated notification, CancellationToken ct)
    {
        Console.WriteLine($"User created: {notification.Name}");
        return ValueTask.CompletedTask;
    }
}

// Usage
await Mediator.Publish(new UserCreated(42, "Alice"), ct);
```

### Parallel Notifications

Apply `[ParallelNotification]` to run all handlers concurrently via `Task.WhenAll`:

```csharp
[ParallelNotification]
public readonly record struct OrderPlaced(int OrderId) : INotification;
```

### Polymorphic Notifications

Handlers for base notification types are automatically included in all matching concrete notifications:

```csharp
// Called for EVERY notification
public class GlobalLogger : INotificationHandler<INotification>
{
    public ValueTask Handle(INotification notification, CancellationToken ct) { ... }
}

// Called only for notifications implementing IOrderNotification
public interface IOrderNotification : INotification { }

public class OrderAuditor : INotificationHandler<IOrderNotification>
{
    public ValueTask Handle(IOrderNotification notification, CancellationToken ct) { ... }
}
```

The generator detects the type hierarchy at compile time and inlines base handlers into the appropriate `Publish()` methods. The concrete notification is passed directly (works via contravariance on `INotificationHandler<in TNotification>`).

### Streaming

```csharp
public readonly record struct CountTo(int Max) : IStreamRequest<int>;

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

// Usage
await foreach (var n in Mediator.CreateStream(new CountTo(5), ct))
{
    Console.Write($"{n} ");
}
```

### Pipeline Behaviors

Pipeline behaviors wrap request handlers with cross-cutting concerns. They are inlined at compile time as nested static calls — no allocation.

```csharp
[PipelineBehavior(Order = 0)]
public class LoggingBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        return next(request, ct);
    }
}
```

Scope a behavior to a specific request type:

```csharp
[PipelineBehavior(Order = 1, AppliesTo = typeof(CreateUser))]
public class CreateUserAudit : IPipelineBehavior { ... }
```

### Handler Dependencies

Configure factory delegates for handlers that need dependencies:

```csharp
Mediator.Configure(cfg =>
{
    cfg.SetFactory<CreateUserHandler>(() => new CreateUserHandler(myDbContext));
});
```

### Dependency Injection

The generator emits an `IMediator` interface and `MediatorService` class with strongly-typed overloads that delegate to the static `Mediator`. This gives you constructor injection and testability with near-zero overhead (one virtual call — the JIT can often devirtualize):

```csharp
// Registration
services.AddSingleton<IMediator, MediatorService>();

// Injection
public class OrderController(IMediator mediator)
{
    public async Task PlaceOrder(Order order, CancellationToken ct)
    {
        var id = await mediator.Send(new CreateOrder(order), ct);
        await mediator.Publish(new OrderPlaced(id), ct);
    }
}
```

The interface contains the same strongly-typed `Send`, `Publish`, and `CreateStream` overloads as the static class — no boxing, no runtime type dispatch.

## Analyzer Diagnostics

| ID | Severity | Description |
|---|---|---|
| ZAM001 | Error | Request type has no registered handler |
| ZAM002 | Error | Request type has multiple handlers |
| ZAM003 | Warning | Request type is a class — use `readonly record struct` |
| ZAM004 | Error | Handler method signature doesn't match expected pattern |
| ZAM005 | Error | Pipeline behavior missing static `Handle<TRequest, TResponse>` method |
| ZAM006 | Warning | Duplicate `[PipelineBehavior(Order)]` values — ambiguous ordering |
| ZAM007 | Error | Stream handler returns wrong type instead of `IAsyncEnumerable` |

## Benchmarks

### ZeroAlloc.Mediator vs MediatR

_BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.7840) · 12th Gen Intel Core i9-12900HK · .NET 10.0.3_

Ratio is relative to the ZeroAlloc.Mediator baseline for each category.

| Method | Category | Mean | Ratio | Allocated | Alloc Ratio |
|---|---|---:|---:|---:|---:|
| ZeroAllocMediator_Publish_Single | Publish1 | 5.907 ns | 1.06 | 0 B | NA |
| MediatR_Publish_Single | Publish1 | 221.578 ns | 39.61 | 792 B | NA |
| ZeroAllocMediator_Publish_Multi | Publish2 | 5.273 ns | 1.01 | 0 B | NA |
| MediatR_Publish_Multi | Publish2 | 299.262 ns | 57.41 | 1,032 B | NA |
| ZeroAllocMediator_Send | Send | 1.883 ns | 1.62 | 0 B | NA |
| MediatR_Send | Send | 75.159 ns | 64.69 | 224 B | NA |
| ZeroAllocMediator_Send_Static | SendDI | 2.539 ns | 1.29 | 0 B | NA |
| ZeroAllocMediator_Send_DI | SendDI | 1.339 ns | 0.68 | 0 B | NA |
| MediatR_Send_DI | SendDI | 88.618 ns | 44.90 | 224 B | NA |
| ZeroAllocMediator_SendPipeline | SendPipeline | 2.961 ns | 1.14 | 0 B | NA |
| MediatR_SendPipeline | SendPipeline | 76.742 ns | 29.51 | 152 B | NA |
| ZeroAllocMediator_Stream | Stream | 149.316 ns | 1.02 | 104 B | 1.00 |
| MediatR_Stream | Stream | 449.751 ns | 3.06 | 528 B | 5.08 |

ZeroAlloc.Mediator is **26-65x faster** than MediatR with **zero allocation** on all synchronous paths. The DI interface (`IMediator`) adds no measurable overhead vs the static API — both complete in ~1-3 ns with 0 bytes allocated. MediatR allocates 152-1,032 bytes per call due to DI resolution, delegate creation, and `Task<T>` boxing.

## Project Structure

```
ZeroAlloc.Mediator/
├── src/
│   ├── ZeroAlloc.Mediator/               # Core abstractions
│   └── ZeroAlloc.Mediator.Generator/     # Source generator
├── tests/
│   ├── ZeroAlloc.Mediator.Tests/
│   └── ZeroAlloc.Mediator.Benchmarks/
└── samples/
    └── ZeroAlloc.Mediator.Sample/
```

## How It Works

The source generator:

1. Discovers all handler types via Roslyn `CreateSyntaxProvider` pipelines
2. Validates handler signatures and reports diagnostics at compile time
3. Emits a static `Mediator` class with strongly-typed overloads per request/notification/stream type
4. Inlines pipeline behaviors as nested static lambda calls
5. Resolves notification type hierarchies for polymorphic dispatch

No reflection, no runtime scanning, no allocations per dispatch.

## License

MIT
