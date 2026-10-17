# ZeroAlloc.Mediator

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Mediator.svg)](https://www.nuget.org/packages/ZeroAlloc.Mediator)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Mediator/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Mediator)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

A source-generated MediatR alternative for .NET 8 and .NET 10: requests, notifications and streams dispatched without reflection, with no allocations on the `Send` and `Publish` hot path, ready for NativeAOT.

## Why ZeroAlloc.Mediator

- **0 B per `Send` and `Publish`.** The static `Mediator` path, pipeline behaviors included, is held at zero bytes by `AllocationGate` tests. MediatR allocates 224 B per `Send` and 792 B per single-handler `Publish` in the same benchmark.
- **3.9–20x faster than MediatR** on `Send` and `Publish` in the same BenchmarkDotNet run. Through `IMediator` resolved from a new scope per call, the way an ASP.NET Core request does, it is still 1.4–3.2x faster and allocates 176–200 B against MediatR's 352–1,232 B.
- **Works under NativeAOT and trimming.** Dispatch is a generated, strongly-typed overload per request type: no reflection, no dictionary lookup, no handler scanning at startup. CI publishes and runs a NativeAOT sample on every build.
- **Mistakes fail the build, not the request.** A request with no handler, two handlers for one request or a misconfigured behavior is reported by the generator as a compiler diagnostic.

## Install

```bash
dotnet add package ZeroAlloc.Mediator
dotnet add package ZeroAlloc.Mediator.Generator
```

The second package is the source generator; it is added as a private development dependency. See [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/getting-started.md#installation) for multi-project solutions.

## Quick start

```csharp
using ZeroAlloc.Mediator;

decimal total = await Mediator.Send(new AddToCart(Quantity: 3, UnitPrice: 9.99m));
Console.WriteLine(total); // 29.97

public readonly record struct AddToCart(int Quantity, decimal UnitPrice) : IRequest<decimal>;

public sealed class AddToCartHandler : IRequestHandler<AddToCart, decimal>
{
    public ValueTask<decimal> Handle(AddToCart request, CancellationToken ct)
        => ValueTask.FromResult(request.Quantity * request.UnitPrice);
}
```

`Mediator.Send(AddToCart)` is an overload the generator wrote for this request, so the call is checked by the compiler and dispatches straight to the handler. In an ASP.NET Core app, call `builder.Services.AddMediator()` and inject `IMediator`; see [Dependency Injection](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md).

## What you get

- **Requests and responses**: commands and queries with a typed response, or `Unit` for none.
- **Notifications**: publish to every handler, sequentially or in parallel with `[ParallelNotification]`, and handlers for a base interface receive derived notifications too.
- **Streaming**: `IAsyncEnumerable<T>` results through `CreateStream`.
- **Pipeline behaviors**: logging, validation, transactions and other cross-cutting code, inlined into each dispatch at compile time instead of iterated from a list.
- **Dependency injection**: a generated `AddMediator()` and `IMediator`, with handler lifetimes you choose.
- **Analyzer diagnostics**: ZAM001–ZAM011, with fixes, in the [diagnostics reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/diagnostics.md).

Optional packages plug into the pipeline; reference one and call its `With...()` method on `AddMediator()`:

- [`ZeroAlloc.Mediator.Validation`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md#bridge-packages): runs the ZeroAlloc.Validation validator for a request before its handler. `WithValidation()`
- [`ZeroAlloc.Mediator.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/authorization.md): gates dispatch on `[RequirePolicy]` checks from ZeroAlloc.Authorization. `WithAuthorization()`
- [`ZeroAlloc.Mediator.Cache`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md#bridge-packages): caches responses for requests marked `[CacheResponse]`. `WithCache()`
- [`ZeroAlloc.Mediator.Resilience`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md#bridge-packages): retry, timeout and circuit-breaker policies through ZeroAlloc.Resilience. `WithResilience()`
- [`ZeroAlloc.Mediator.Telemetry`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/dependency-injection.md#bridge-packages): OpenTelemetry request count and duration metrics. `WithTelemetry()`

## Performance

.NET 10, BenchmarkDotNet, ZeroAlloc.Mediator and MediatR in the same run. Allocations are exact; speed is a same-run ratio.

| Operation | Speed vs MediatR | ZeroAlloc.Mediator | MediatR |
|---|---:|---:|---:|
| `Send`, static `Mediator` | 3.9x faster | 0 B | 224 B |
| `Publish`, 1 handler, static `Mediator` | 16x faster | 0 B | 792 B |
| `Send` via `IMediator`, new DI scope per call | 1.5x faster | 176 B | 496 B |

The 176 B is the DI scope and the transient `IMediator`, not the dispatch. The static rows hold with no factory registered, for a handler without instance state. The [performance page](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/performance.md) has every row, including pipelines and streaming, and explains where each byte comes from.

## Learn more

- **Documentation**: [mediator.zeroalloc.net](https://mediator.zeroalloc.net), or the [docs folder](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/docs/README.md) on GitHub, including migration guides.
- **Related ZeroAlloc packages**:
  - [ZeroAlloc.Pipeline](https://github.com/ZeroAlloc-Net/ZeroAlloc.Pipeline) ([docs](https://pipeline.zeroalloc.net)): Mediator's pipeline behaviors are ZeroAlloc.Pipeline behaviors, and its generator inlines them.
  - [ZeroAlloc.Validation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation) ([docs](https://validation.zeroalloc.net)): source-generated validators that `ZeroAlloc.Mediator.Validation` runs before each handler.
  - [ZeroAlloc.Authorization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) ([docs](https://authorization.zeroalloc.net)): the policies that `ZeroAlloc.Mediator.Authorization` checks before dispatch.
  - [ZeroAlloc.Results](https://github.com/ZeroAlloc-Net/ZeroAlloc.Results) ([docs](https://results.zeroalloc.net)): give a request a `Result` response and the validation and authorization packages return failures in it instead of throwing.
- **Contributing**: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues).
- **License**: MIT, see [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/blob/main/LICENSE).
