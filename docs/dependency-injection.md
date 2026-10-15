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

Register the mediator. The generated `AddMediator()` also registers every handler in the assembly:

> This single-project shape calls `AddMediator()` from the host. That works because the handlers live in the same assembly. If your handlers are in a separate application assembly, see [`AddMediator()` is internal too](#addmediator-is-internal-too--registration-lives-with-the-handlers) — the call has to move there.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddMediator();

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

## Handler lifetimes

The generated `AddMediator()` registers every request, notification and stream handler in the assembly, each as its concrete type. Handlers are **Transient** by default, matching MediatR.

- `AddMediator(ServiceLifetime.Scoped)` changes the default for every handler.
- `[HandlerLifetime(ServiceLifetime.X)]` on a handler wins over the default.
- ZeroAlloc.Inject's `[Transient]`, `[Scoped]` and `[Singleton]` on a handler are honoured too. Mediator matches them by name, so it does not depend on ZeroAlloc.Inject.
- The precedence is `[HandlerLifetime]`, then a ZeroAlloc.Inject lifetime attribute, then the `AddMediator` default. A handler that carries both kinds of attribute gets the `[HandlerLifetime]` lifetime.
- Handlers are added with `TryAdd`, so only a registration you made *before* `AddMediator()` is kept. A `TryAdd` after it is a no-op, and that includes ZeroAlloc.Inject's generated `Add<Assembly>Services()`. Set lifetimes with `[HandlerLifetime]`, ZeroAlloc.Inject's lifetime attributes or `AddMediator(lifetime)` rather than by registering the handler again afterwards.
- Internal handlers are registered too. Abstract handler classes, open-generic handlers and handlers without a public constructor are not. The container builds a type only through a public constructor, so register such a handler yourself, for example with a factory.
- Registration happens at compile time, so it is trim- and AOT-safe.

```csharp
// Every handler is Scoped unless it says otherwise.
builder.Services.AddMediator(ServiceLifetime.Scoped);

// This one is Singleton, whatever the default is.
[HandlerLifetime(ServiceLifetime.Singleton)]
public sealed class GetSettingsHandler : IRequestHandler<GetSettings, Settings> { /* ... */ }
```

If your app builds its provider with `ValidateOnBuild` (the ASP.NET Core Development default), note that internal handlers are now registered. An internal handler whose constructor dependencies are not registered fails validation at startup.

## Migrating from RegisterHandlersFromAssembly

`RegisterHandlersFromAssembly` and `RegisterHandlersFromAssemblies` are `[Obsolete]`, are not trim- or AOT-safe, and will be removed in the next major version. To migrate:

1. Delete the `.RegisterHandlersFromAssembly(...)` call. `AddMediator()` now registers the handlers.
2. If you passed a lifetime, pass it to `AddMediator(lifetime)` instead.

A scanner call left in place no longer sets the lifetime. The generated registration runs first, so the scanner's `TryAdd` is a no-op: a leftover `services.AddMediator().RegisterHandlersFromAssembly(asm, ServiceLifetime.Scoped)` yields Transient handlers, except those carrying `[HandlerLifetime]` or a ZeroAlloc.Inject lifetime attribute.

```csharp
// Before
services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly, ServiceLifetime.Scoped);

// After
services.AddMediator(ServiceLifetime.Scoped);
```

## Lifetimes

### `IMediator`

Registered as **Transient**. The mediator instance itself is stateless; what matters is the `IServiceProvider` it captures, which is whichever scope you injected from. Transient is strictly safer than Scoped here — a Singleton consumer can inject `IMediator` without triggering a captive-dependency warning. This is a behavioral match for MediatR.

### Handlers

Handlers are **Transient** by default, matching MediatR. See [Handler lifetimes](#handler-lifetimes) for how to change the default or pick a lifetime per handler.

Override per-handler with the `[HandlerLifetime]` attribute. The attribute always wins over the `AddMediator` default:

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

- **Handler has an accessible parameterless constructor** — no registration required. When the handler is stateless, with no instance fields, auto-properties or primary-constructor parameters on it or its base classes, the generator caches one instance and reuses it on every call, so the call allocates nothing. A handler that holds state gets `?? new T()` as the fallback, a new instance per call, so its state never leaks between calls.
- **Handler has only parameterised constructors** — the generator emits `?? throw new InvalidOperationException(...)` as the fallback, and the analyzer raises [ZAM008](diagnostics.md#zam008--handler-has-no-parameterless-constructor) at build time. The static call will throw at runtime unless you register a factory.

Register a factory at startup for static-path handlers that need DI:

```csharp
Mediator.Configure(c =>
{
    c.SetFactory<GetProductHandler>(() => sp.GetRequiredService<GetProductHandler>());
});
```

Once registered, `Mediator.Send(new GetProductQuery(...))` invokes the factory in place of `new GetProductHandler()`. The factory delegate is shared with `IMediator` for handlers that have one.

## Visibility — the generated types are `internal` to your assembly

`IMediator`, `MediatorService`, `MediatorConfig` and the static `Mediator` class are emitted by the generator into the assembly being compiled, not shipped in the `ZeroAlloc.Mediator` package. Their members are derived from the handlers discovered in *that* assembly — one strongly-typed `Send` overload per request handler, `Publish` per notification, `CreateStream` per stream request. That is what makes dispatch reflection-free.

Because every assembly gets a differently-shaped copy, all of them are emitted **`internal`**. Two assemblies that both run the generator can then be referenced from a third project without their copies colliding.

The practical consequence: `IMediator` cannot appear in a `public` signature, or the compiler reports **CS0051** ("inconsistent accessibility").

```csharp
// Won't compile — public type exposing an internal parameter.
public sealed class OrderService(IMediator mediator);

// Fine — the consuming type is internal too.
internal sealed class OrderService(IMediator mediator);

// Fine — lambdas have no declared accessibility.
app.MapGet("/orders", async (IMediator mediator, CancellationToken ct) => …);
```

This is a constraint of the design rather than a limitation to work around: assembly A's `IMediator` and assembly B's `IMediator` are different types with different members, so a signature sharing one across a boundary was never meaningful. Types that need to cross assemblies should expose your own abstraction and take `IMediator` internally.

### `AddMediator()` is internal too — registration lives with the handlers

`AddMediator()` is emitted into the same `internal static partial class MediatorServiceCollectionExtensions`, so it carries the same accessibility as the types above:

```csharp
internal static partial class MediatorServiceCollectionExtensions   // namespace Microsoft.Extensions.DependencyInjection
```

It can only be called from the assembly that owns the handlers. In a single-project application that is also the host, so the example at the top of this page compiles as written. **In a layered solution it does not** — with handlers in an application assembly and `Program.cs` in a separate web host, calling `builder.Services.AddMediator()` from the host fails to compile, and the error points at the call site rather than at the cause.

Give the application assembly its own public registration method and call that from the host:

```csharp
// Application assembly — public, alongside the handlers.
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddMediator()                       // internal, same assembly — fine
                .Services;
}

// Host — Program.cs
builder.Services.AddApplication();
```

This follows from the same reasoning as `IMediator` being internal: the registration extension configures *that* assembly's generated mediator, so it belongs in that assembly.


### MVC controllers

MVC controllers must be `public` to be discovered, so they cannot take `IMediator` in their constructor. Minimal APIs are unaffected — endpoint lambdas have no declared accessibility — so prefer them where you can. Where you need a controller, put an internal seam behind it:

```csharp
internal interface IOrderCommands
{
    ValueTask<OrderId> PlaceAsync(CreateOrder cmd, CancellationToken ct);
}

internal sealed class OrderCommands(IMediator mediator) : IOrderCommands
{
    public ValueTask<OrderId> PlaceAsync(CreateOrder cmd, CancellationToken ct)
        => mediator.Send(cmd, ct);
}

// The controller is public; its dependency is your own abstraction, not IMediator.
public sealed class OrdersController(IOrderCommands commands) : ControllerBase { … }
```

Registering `IOrderCommands` is an ordinary `services.AddScoped<IOrderCommands, OrderCommands>()` — an internal service type is fine for DI, since the container resolves by type rather than by signature.

## Compatibility — migrating from 3.0.x

- **`IMediator` is now Transient (was Singleton).** Behaviorally identical: dispatch is stateless either way. The only thing that changes is reference equality across resolutions — if you cached the singleton instance and compared with `ReferenceEquals`, that no longer holds. Cached references still work; they just no longer match a fresh `GetRequiredService<IMediator>()` call.
- **Drop the `null!` shim constructors.** Handlers that previously declared `internal MyHandler() : this(null!, null!) { }` purely to satisfy the unconditional `?? new T()` fallback can delete that ctor. ZAM008 will tell you if any remaining static-path call site still needs it.
- **`Mediator.Configure(c => c.SetFactory<...>(...))` is unchanged.** Existing 3.0.x setups that wire factories manually keep working without modification. Handlers you register by hand before `AddMediator()` keep working, because `AddMediator()` uses `TryAdd` and both registration paths populate the same dispatch tables. Only registrations made *before* `AddMediator()` are kept: a `TryAdd` after it is a no-op, so set lifetimes with `[HandlerLifetime]`, ZeroAlloc.Inject's lifetime attributes or `AddMediator(lifetime)`.

## Bridge packages

`AddMediator()` returns an `IMediatorBuilder` that the bridge packages extend with `WithXxx()` helpers. Cache, validation, resilience, and telemetry are such extensions.

Referencing a bridge package is what puts its behavior into the pipeline: the source generator finds the behavior in the referenced assembly, as described in [Behaviors from Referenced Assemblies](pipeline-behaviors.md#behaviors-from-referenced-assemblies). The `WithXxx()` call registers what the behavior needs at run time, so the two go together:

```csharp
services.AddMediator()
        .WithCache()
        .WithValidation()
        .WithResilience()
        .WithTelemetry();
```

`AddMediator()` is idempotent (`TryAdd`); calling it more than once is safe, and the first call's handler lifetimes win. Only registrations made *before* the first `AddMediator()` are kept: a `TryAdd` after it is a no-op, so set lifetimes with `[HandlerLifetime]`, ZeroAlloc.Inject's lifetime attributes or `AddMediator(lifetime)`.

### How a bridge reaches your container

A pipeline behavior's `Handle` is static, so the generated dispatcher can call it without allocating. The behavior has no instance to inject into, so the generated `IMediator` hands it the provider it was resolved from. `IMediator` is transient, so in an ASP.NET Core request, or any other scope, that provider is the scope's. The authorization bridge resolves the caller's `ISecurityContext` and the policies from it, the validation bridge its validators, and the cache bridge the container's `IMemoryCache`. Scoped services therefore come from the request being dispatched, and several containers in one process, for example one per integration test, never see each other's services.

A behavior receives the provider in one of two ways.

**As a `Handle` argument.** Declare a second `Handle` overload that takes an `IServiceProvider` after the request. The generated `IMediator.Send` calls it with its provider; the static `Mediator` class, which has none, calls the overload without one. The bridges do this, and it costs nothing per call:

```csharp
[PipelineBehavior(Order = 10)]
public sealed class AuditBehavior : IPipelineBehavior
{
    // Static Mediator.Send: no request scope.
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => next(request, ct);

    // IMediator.Send: the services of the request being dispatched.
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, IServiceProvider services, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        services.GetService<IAuditLog>()?.Record(request);
        return next(request, ct);
    }
}
```

**Through `PipelineBehaviorServices.Current`.** A behavior with only the classic `Handle` can read the provider from `PipelineBehaviorServices.Current`. The value flows with the async call and reverts when `Send` returns. Setting it is an `AsyncLocal` write, which allocates 72 B per `Send`, so the generated `IMediator.Send` sets it only when a behavior in the request's pipeline may read it: one that has no `IServiceProvider` overload and does not declare `UsesPipelineBehaviorServices = false`. Declare that on every behavior that reads no services, so a pipeline made only of such behaviors and the bridges skips the write:

```csharp
[PipelineBehavior(Order = 20, UsesPipelineBehaviorServices = false)]
public sealed class TimingBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var started = Stopwatch.GetTimestamp();
        try { return await next(request, ct); }
        finally { Metrics.Record(Stopwatch.GetElapsedTime(started)); }
    }
}
```

`UsesPipelineBehaviorServices` is a property of `ZeroAlloc.Mediator.PipelineBehaviorAttribute`; a behavior marked with the base `ZeroAlloc.Pipeline.PipelineBehaviorAttribute` is always treated as one that may read `Current`. `Current` is meant for behaviors: a handler is resolved from the request's scope and injects what it needs, and it should not read `Current`, which is `null` when no behavior in its pipeline needs it.

Resolve `IMediator` from the scope of the work it does. An `IMediator` resolved from the root provider, for example injected into a singleton, hands the pipeline the root provider, and scoped services resolved from the root provider are shared by every request. Enable `ValidateScopes` to have the container reject that.

#### Dispatch through the static `Mediator` class

A request sent through the static `Mediator` class has no request scope, and `PipelineBehaviorServices.Current` is `null`. For these requests each `WithXxx()` registers an `IPipelineBehaviorStateInitializer` that hands the bridge the container, and `AddMediator()` registers a `PipelineBehaviorStateActivation` singleton that runs them. The first `IMediator` resolved from a container resolves that singleton. An app that dispatches only through the static class resolves the activation once after building the container:

```csharp
var provider = services.BuildServiceProvider();
provider.GetRequiredService<PipelineBehaviorStateActivation>();
```

The authorization and validation bridges then open a scope on that container for each request that has policies or a validator, and resolve the scoped services from it, never from the container itself. This state is process-wide: when one process builds several containers, static dispatch uses the container that most recently activated. Disposing a container clears the cache and validation state, but only while it still points at that container. The authorization state is not cleared: a static dispatch after its container is disposed throws `ObjectDisposedException` rather than skipping authorization.

## See also

- **Analyzer reference** — [Compiler Diagnostics](diagnostics.md), including [ZAM008](diagnostics.md#zam008--handler-has-no-parameterless-constructor) for missing-parameterless-ctor handlers.
- **Sample app** — `samples/ZeroAlloc.Mediator.AspNetSample/` is the canonical end-to-end ASP.NET Core example with generated handler registration and a scoped dependency flowing into a handler.
