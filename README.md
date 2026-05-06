# ZeroAlloc.Mediator

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mediator.svg)](https://www.nuget.org/packages/ZeroAlloc.Mediator)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Mediator is a source-generated, zero-allocation mediator for .NET 8 and .NET 10. It supports request/response, notifications, and streaming without reflection or dynamic dispatch. The source generator eliminates the runtime overhead that reflection-based mediators incur by wiring all dispatch at compile time — no dictionaries, no virtual dispatch, no delegate allocation per request.

## Install

```bash
dotnet add package ZeroAlloc.Mediator
```

The generator package must also be added as an analyzer:

```xml
<PackageReference Include="ZeroAlloc.Mediator.Generator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Example

```csharp
// 1. Define a request record and its expected response type
public readonly record struct CreateOrder(string Product, int Qty) : IRequest<OrderId>;

// 2. Implement a handler — no constructor dependencies needed for this example
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>
{
    public ValueTask<OrderId> Handle(CreateOrder request, CancellationToken ct)
        => ValueTask.FromResult(OrderId.NewId());
}

// 3. Register IMediator with DI (the generator emits services.AddMediator() automatically)
services.AddMediator();

// 4. Send the request and use the result — fully strongly-typed, zero allocation
public class OrderController(IMediator mediator)
{
    public async Task<IResult> PlaceOrder(CreateOrder cmd, CancellationToken ct)
    {
        var id = await mediator.Send(cmd, ct);   // returns OrderId, no boxing
        return Results.Created($"/orders/{id}", id);
    }
}
```

## Performance

ZeroAlloc.Mediator is **40–160x faster** than MediatR with **zero heap allocation** on all non-streaming paths (.NET 10.0.4, i9-12900HK, BenchmarkDotNet).

| Operation | ZeroAlloc.Mediator | MediatR | Speedup | Alloc |
|---|---:|---:|---:|---:|
| Send | 0.5 ns | 78.3 ns | ~160x | 0 B vs 224 B |
| Send + pipeline | 2.8 ns | 101.8 ns | ~46x | 0 B vs 152 B |
| Send (via IMediator DI) | 5.8 ns | 86.3 ns | ~15x | 0 B vs 224 B |
| Publish (1 handler) | 6.1 ns | 243.8 ns | ~40x | 0 B vs 792 B |
| Publish (multi handler) | 6.6 ns | 332.4 ns | ~51x | 0 B vs 1,032 B |
| Stream (5 items) | 202.8 ns | 654.4 ns | ~3x | 104 B vs 528 B |

See [docs/performance.md](docs/performance.md) for the full benchmark table and zero-allocation design explanation.

## Features

- **Request/Response** — strongly-typed `Send` overloads per request type
- **Notifications** — sequential or parallel (`[ParallelNotification]`) dispatch
- **Streaming** — `IAsyncEnumerable<T>` via `CreateStream`
- **Pipeline Behaviors** — compile-time inlined middleware chain (logging, validation, etc.)
- **Bridge Packages** — `WithCache()`, `WithValidation()`, `WithResilience()`, `WithTelemetry()` (OpenTelemetry spans + metrics on `IRequest<T>.Send`), `WithAuthorization()` (`[Authorize]`-gated dispatch via `ZeroAlloc.Authorization` policies)
- **Polymorphic Notifications** — base interface handlers are automatically included in concrete notification dispatch
- **Analyzer Diagnostics** — missing handlers, duplicates, and misconfigurations are build errors/warnings
- **Zero Allocation** — `ValueTask`, `readonly record struct`, static dispatch, no closures
- **Native AOT Compatible** — no reflection at runtime; all dispatch is resolved at compile time by the source generator

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](docs/getting-started.md) | Install and send your first request in five minutes |
| [Requests & Handlers](docs/requests.md) | Commands, queries, `Unit` responses, dispatch |
| [Notifications](docs/notifications.md) | Events: sequential, parallel, polymorphic handlers |
| [Streaming](docs/streaming.md) | `IAsyncEnumerable<T>` for large result sets |
| [Pipeline Behaviors](docs/pipeline-behaviors.md) | Compile-time middleware: logging, validation, caching |
| [Authorization](docs/authorization.md) | `[Authorize]`-gated dispatch via `ZeroAlloc.Authorization` policies |
| [Dependency Injection](docs/dependency-injection.md) | DI containers, `IMediator`, factory delegates |
| [Diagnostics](docs/diagnostics.md) | ZAM001–ZAM007 compiler error reference with fixes |
| [Performance](docs/performance.md) | Zero-alloc internals, benchmark results, Native AOT |
| [Advanced Patterns](docs/advanced.md) | Error handling, cancellation, scoped behaviors |
| [Testing](docs/testing.md) | Unit-test handlers, behaviors, and notifications |

## License

MIT
