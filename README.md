# ZeroAlloc.Mediator

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mediator.svg)](https://www.nuget.org/packages/ZeroAlloc.Mediator)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

ZeroAlloc.Mediator is a source-generated, zero-allocation mediator for .NET 8 and .NET 10. It supports request/response, notifications, and streaming without reflection or dynamic dispatch. The source generator eliminates the runtime overhead that reflection-based mediators incur by wiring all dispatch at compile time — no dictionaries, no virtual dispatch, no delegate allocation per request.

## Install

```bash
dotnet add package ZeroAlloc.Mediator
```

The generator package must also be added as an analyzer:

```xml
<PackageReference Include="ZeroAlloc.Mediator.Generator" Version="*" OutputItemType="Analyzer" ReferenceOutputAssembly="false" PrivateAssets="all" />
```

Optional bridge packages add pipeline behaviors for other ZeroAlloc libraries:

| Package | Adds |
|---|---|
| `ZeroAlloc.Mediator.Cache` | `[Cache]`-driven response caching through ZeroAlloc.Cache — `WithCache()` |
| `ZeroAlloc.Mediator.Validation` | Request validation through ZeroAlloc.Validation `ValidatorFor<T>` — `WithValidation()` |
| `ZeroAlloc.Mediator.Resilience` | Retry, timeout and circuit-breaker policies through ZeroAlloc.Resilience — `WithResilience()` |
| `ZeroAlloc.Mediator.Telemetry` | OpenTelemetry metrics for `Send` — `WithTelemetry()` |
| `ZeroAlloc.Mediator.Authorization` | `[Authorize]`-gated dispatch through ZeroAlloc.Authorization policies — `WithAuthorization()` |

```bash
dotnet add package ZeroAlloc.Mediator.Cache   # or .Validation, .Resilience, .Telemetry, .Authorization
```

`PrivateAssets="all"` matters in multi-project solutions: without it the analyzer flows transitively across a `ProjectReference`, so a downstream project runs the generator a second time and gets **CS0436** on the duplicate generated types — a hard failure under `TreatWarningsAsErrors`.

Request types should be `readonly record struct`. A reference-type `record` still works, but the generator reports **ZAM003** (a warning, which `TreatWarningsAsErrors` turns into a build failure) because dispatch then allocates.

## Example

```csharp
using ZeroAlloc.Mediator;

var builder = WebApplication.CreateBuilder(args);

// 3. Register IMediator with DI (the generator emits services.AddMediator() automatically)
builder.Services.AddMediator();

var app = builder.Build();

// 4. Send the request and use the result — fully strongly-typed, no boxing
app.MapPost("/orders", async (CreateOrder cmd, IMediator mediator, CancellationToken ct) =>
{
    var id = await mediator.Send(cmd, ct);   // returns OrderId
    return Results.Created($"/orders/{id.Value}", id);
});

app.Run();

// 1. Define a request record and its expected response type
public readonly record struct OrderId(Guid Value);
public readonly record struct CreateOrder(string Product, int Qty) : IRequest<OrderId>;

// 2. Implement a handler — no constructor dependencies needed for this example
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>
{
    public ValueTask<OrderId> Handle(CreateOrder request, CancellationToken ct)
        => ValueTask.FromResult(new OrderId(Guid.NewGuid()));
}
```

> `IMediator` is generated into your assembly and is `internal`, so it cannot appear in a
> `public` signature. Endpoint lambdas and `internal` services are unaffected; see
> [Visibility](docs/dependency-injection.md#visibility--the-generated-types-are-internal-to-your-assembly)
> for the reasoning and the pattern for MVC controllers.

## Performance

The static `Mediator` class is **36–160x faster** than MediatR on `Send` and `Publish`, with **zero heap allocation** on those paths (.NET 10, i9-12900HK, BenchmarkDotNet). Through `IMediator` resolved from a new scope per call, the way an ASP.NET Core request does, it is 1.2–1.5x faster and allocates 176 B for the scope and the transient `MediatorService`.

| Operation | ZeroAlloc.Mediator | MediatR | Speedup | Alloc |
|---|---:|---:|---:|---:|
| Send | 0.5 ns | 78.3 ns | ~160x | 0 B vs 224 B |
| Send + pipeline | 2.8 ns | 101.8 ns | ~36x | 0 B vs 152 B |
| Publish (1 handler) | 6.1 ns | 243.8 ns | ~40x | 0 B vs 792 B |
| Publish (multi handler) | 6.6 ns | 332.4 ns | ~50x | 0 B vs 1,032 B |
| Send (via IMediator DI, scope per call) | 160.1 ns | 187.2 ns | ~1.2x | 176 B vs 496 B |
| Send + pipeline (via IMediator DI, scope per call) | 126.9 ns | 191.2 ns | ~1.5x | 176 B vs 352 B |
| Stream (5 items) | 202.8 ns | 654.4 ns | ~3x | 104 B vs 528 B |

The 0 B rows are pinned by `AllocationGate` tests with cached handler instances. A handler that the static `Mediator` creates through its parameterless-constructor fallback is a new object on every call, 24 B for an empty class; the .NET 10 JIT removed it in this benchmark, but that is not guaranteed, and .NET 8 keeps it. For a guaranteed 0 B, hand the static path a cached instance with `Mediator.Configure(c => c.SetFactory(() => handler))`, or resolve singleton handlers through `IMediator` ([#328](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/328)).

The 104 B on the Stream row is the C# compiler's `async IAsyncEnumerable<T>` state machine for your handler method — every mediator dispatching to such handlers pays this; ZA.Mediator's own stream-dispatch contribution is 0 B. See [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/performance.md) for the full benchmark table and zero-allocation design explanation.

## Features

- **Request/Response** — strongly-typed `Send` overloads per request type
- **Notifications** — sequential or parallel (`[ParallelNotification]`) dispatch
- **Streaming** — `IAsyncEnumerable<T>` via `CreateStream`
- **Pipeline Behaviors** — compile-time inlined middleware chain (logging, validation, etc.)
- **Bridge Packages** — `WithCache()`, `WithValidation()`, `WithResilience()`, `WithTelemetry()` (OpenTelemetry metrics on `IRequest<T>.Send`; every dispatch opens a span with or without it), `WithAuthorization()` (`[Authorize]`-gated dispatch via `ZeroAlloc.Authorization` policies)
- **Polymorphic Notifications** — base interface handlers are automatically included in concrete notification dispatch
- **Analyzer Diagnostics** — missing handlers, duplicates, and misconfigurations are build errors/warnings
- **Zero Allocation** — `ValueTask`, `readonly record struct`, static dispatch, no closures; `Send` and `Publish` gated at 0 B by `AllocationGate` tests
- **Native AOT Compatible** — no reflection at runtime; all dispatch is resolved at compile time by the source generator

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/getting-started.md) | Install and send your first request in five minutes |
| [Requests & Handlers](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/requests.md) | Commands, queries, `Unit` responses, dispatch |
| [Notifications](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/notifications.md) | Events: sequential, parallel, polymorphic handlers |
| [Streaming](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/streaming.md) | `IAsyncEnumerable<T>` for large result sets |
| [Pipeline Behaviors](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/pipeline-behaviors.md) | Compile-time middleware: logging, validation, caching |
| [Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/authorization.md) | `[Authorize]`-gated dispatch via `ZeroAlloc.Authorization` policies |
| [Dependency Injection](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md) | DI containers, `IMediator`, factory delegates |
| [Diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/diagnostics.md) | ZAM001–ZAM011 compiler error reference with fixes |
| [Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/performance.md) | Zero-alloc internals, benchmark results, Native AOT |
| [Advanced Patterns](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/advanced.md) | Error handling, cancellation, scoped behaviors |
| [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/testing.md) | Unit-test handlers, behaviors, and notifications |
| [Migrating to 6.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/migrating-to-v6.md) | Removed v1 registration shims, Validation 2, retired diagnostics |

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/LICENSE).
