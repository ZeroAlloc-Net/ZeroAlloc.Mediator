---
id: dependency-injection
title: Dependency Injection
slug: /docs/dependency-injection
description: Two parallel dispatch paths in 3.0 — static Mediator for zero-alloc, IMediator for DI-resolved handlers.
sidebar_position: 6
---

# Dependency Injection

ZeroAlloc.Mediator 3.0 exposes two parallel dispatch paths and lets you choose per call site. Use the static `Mediator.Send/Publish/CreateStream` methods for zero-allocation, no-DI scenarios such as libraries, workers, or hot loops. Inject `IMediator` for ASP.NET Core, hosted services, or anywhere your handlers need scoped services. Both paths coexist in the same project — the static API stays free of DI awareness, while the injected `IMediator` resolves handlers from the caller's `IServiceProvider` scope.

## Quickstart — ASP.NET Core

Register the mediator and scan the entry-assembly for handlers:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

app.MapGet("/who", async (IMediator mediator, CancellationToken ct) =>
    (await mediator.Send(new GetRequestId(), ct)).ToString());

app.Run();
```

Handlers are resolved from the per-request `IServiceProvider`, so any scoped dependency (DbContext, request-scoped accessor, current user) flows in for free. Full end-to-end example: `samples/ZeroAlloc.Mediator.AspNetSample/`.

## Quickstart — Worker / hosted service

`IMediator` is registered as Transient and consumes the *caller's* scope. A `BackgroundService` is rooted in the singleton scope, so you must create a per-message scope yourself before resolving `IMediator`:

```csharp
public sealed class OrderProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageQueue _queue;

    public OrderProcessingWorker(IServiceScopeFactory scopeFactory, IMessageQueue queue)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var message in _queue.ReadAllAsync(ct))
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            await mediator.Send(new PlaceOrderCommand(message.CustomerId, message.Items), ct);
            await _queue.AckAsync(message.Id, ct);
        }
    }
}
```

The `using` block bounds the scope to a single message — the same lifetime model ASP.NET Core uses for an HTTP request. Any `Scoped` handler or dependency is created on entry and disposed on exit.

## Lifetimes

### `IMediator`

Registered as **Transient**. The mediator instance itself is stateless; what matters is the `IServiceProvider` it captures, which is whichever scope you injected from. Transient is strictly safer than Scoped here — a Singleton consumer can inject `IMediator` without triggering a captive-dependency warning. This is a behavioral match for MediatR.

### Handlers

The default lifetime for handlers registered through `RegisterHandlersFromAssembly` is **Transient**, matching MediatR. Override the project-wide default with the second argument:

```csharp
services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly, ServiceLifetime.Scoped);
```

Override per-handler with the `[HandlerLifetime]` attribute. The attribute always wins over the registration default:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

[HandlerLifetime(ServiceLifetime.Scoped)]
public sealed class GetOrderHandler : IRequestHandler<GetOrderQuery, Order>
{
    private readonly AppDbContext _db;
    public GetOrderHandler(AppDbContext db) => _db = db;

    public async ValueTask<Order> Handle(GetOrderQuery q, CancellationToken ct)
        => await _db.Orders.FindAsync([q.Id], ct) ?? throw new NotFoundException();
}
```

**When to choose Scoped:** when the handler must share state with the rest of the request — typically a `DbContext` or a unit-of-work — so multiple `Send` calls in the same HTTP request reuse the same instance and see each other's tracked changes. Transient is the right default for stateless handlers and avoids cross-call leaks.

## Static `Mediator.Send` — the zero-alloc path

The static dispatcher is unchanged in 3.0 and remains the right choice for libraries, perf-critical code, and any host that doesn't want a DI container in the path. Handler construction is decided at compile time:

- **Handler has an accessible parameterless constructor** — the generator emits `?? new T()` as the fallback. No registration required.
- **Handler has only parameterised constructors** — the generator emits `?? throw new InvalidOperationException(...)` as the fallback, and the analyzer raises [ZAM008](diagnostics.md#zam008--handler-has-no-parameterless-constructor) at build time. The static call will throw at runtime unless you register a factory.

Register a factory at startup for static-path handlers that need DI:

```csharp
Mediator.Configure(c =>
{
    c.SetFactory<GetProductHandler>(() => sp.GetRequiredService<GetProductHandler>());
});
```

Once registered, `Mediator.Send(new GetProductQuery(...))` invokes the factory in place of `new GetProductHandler()`. The factory delegate is shared with `IMediator` for handlers that have one.

## Compatibility — migrating from 3.0.x

- **`IMediator` is now Transient (was Singleton).** Behaviorally identical: dispatch is stateless either way. The only thing that changes is reference equality across resolutions — if you cached the singleton instance and compared with `ReferenceEquals`, that no longer holds. Cached references still work; they just no longer match a fresh `GetRequiredService<IMediator>()` call.
- **Drop the `null!` shim constructors.** Handlers that previously declared `internal MyHandler() : this(null!, null!) { }` purely to satisfy the unconditional `?? new T()` fallback can delete that ctor. ZAM008 will tell you if any remaining static-path call site still needs it.
- **`Mediator.Configure(c => c.SetFactory<...>(...))` is unchanged.** Existing 3.0.x setups that wire factories manually keep working without modification. You can adopt `RegisterHandlersFromAssembly` incrementally — both registration paths populate the same dispatch tables.

## Bridge packages

`AddMediator()` returns an `IMediatorBuilder` that the bridge packages extend with `WithXxx()` helpers. `RegisterHandlersFromAssembly` is one such extension; cache, validation, resilience, and telemetry are others:

```csharp
services.AddMediator()
        .RegisterHandlersFromAssembly(typeof(Program).Assembly)
        .WithCache()
        .WithValidation()
        .WithResilience()
        .WithTelemetry();
```

`AddMediator()` is idempotent (`TryAddTransient`); calling it more than once is safe.

## See also

- **Analyzer reference** — [Compiler Diagnostics](diagnostics.md), including [ZAM008](diagnostics.md#zam008--handler-has-no-parameterless-constructor) for missing-parameterless-ctor handlers.
- **Sample app** — `samples/ZeroAlloc.Mediator.AspNetSample/` is the canonical end-to-end ASP.NET Core example with assembly-scan registration and a scoped dependency flowing into a handler.
