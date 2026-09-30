---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZAM001–ZAM008 Roslyn analyzer rules with triggers, severities, and fix guidance.
sidebar_position: 7
---

# Compiler Diagnostics

ZeroAlloc.Mediator validates your mediator setup at compile time using a Roslyn analyzer. Misconfigurations appear as build errors or warnings in your IDE and on `dotnet build` — you never discover them at runtime in production.

## Diagnostic Reference Table

| Code | Severity | Title | When it triggers |
|------|----------|-------|------------------|
| ZAM001 | Error | No handler for request | A type implements `IRequest<T>` but has no matching `IRequestHandler` in the project |
| ZAM002 | Error | Multiple handlers for request | More than one `IRequestHandler<TRequest, TResponse>` for the same request type |
| ZAM003 | Warning | Request type is a class | A request type is a `class` instead of `readonly record struct` |
| ZAM004 | — | *Removed in 6.0* | Never reported; the C# compiler enforces it. See [Removed diagnostics](#removed-diagnostics) |
| ZAM005 | Error | Pipeline behavior missing Handle method | A `[PipelineBehavior]` class that implements `IPipelineBehavior` has no public static `Handle<TRequest,TResponse>` method |
| ZAM006 | Warning | Duplicate pipeline behavior Order | Two behaviors have the same `Order` value |
| ZAM007 | — | *Removed in 6.0* | Never reported; the C# compiler enforces it. See [Removed diagnostics](#removed-diagnostics) |
| ZAM008 | Warning | Handler has no parameterless constructor | A handler class has only parameterised constructors and would throw on the static dispatch path |

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

**What it means:** A class marked `[PipelineBehavior]` that implements `IPipelineBehavior` has no `public static` `Handle` method with two type parameters, `Handle<TRequest, TResponse>`.

**Example:**
```csharp
// ❌ Triggers ZAM005 — Handle is an instance method, and the generator only calls static ones
[PipelineBehavior(Order = 0)]
public sealed class LoggingBehavior : IPipelineBehavior
{
    public async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    {
        Console.WriteLine($"[START] {typeof(TRequest).Name}");
        return await next(request, ct);
    }
}
```

**Fix:** Make `Handle` static:
```csharp
[PipelineBehavior(Order = 0)]
public sealed class LoggingBehavior : IPipelineBehavior
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

The method must have this shape:
- Access: `public static`
- Generic: `Handle<TRequest, TResponse>`
- Parameters: `(TRequest, CancellationToken, Func<TRequest, CancellationToken, ValueTask<TResponse>>)`
- Return: `ValueTask<TResponse>`

ZAM005 checks the first two. A `public static Handle<TRequest, TResponse>` with other parameters is not reported as ZAM005; the generated `Send` fails to compile instead.

ZAM005 is only reported for a class the generator picked up as a behavior, which requires `IPipelineBehavior`. A `static class`, or a class without `IPipelineBehavior`, is skipped without any diagnostic, so the behavior never runs. See [Pitfall 1](pipeline-behaviors.md#common-pitfalls).

## ZAM006 — Duplicate Pipeline Behavior Order

**What it means:** Two behaviors share the same `Order` value. The generator emits them in source-order, but that's an implementation detail — don't rely on it.

**Example:**
```csharp
// ❌ Both Order=10 — ZAM006 warning
[PipelineBehavior(Order = 10)]
public sealed class ValidationBehavior : IPipelineBehavior { ... }

[PipelineBehavior(Order = 10)]
public sealed class CachingBehavior : IPipelineBehavior { ... }
```

**Fix:** Use unique values:
```csharp
[PipelineBehavior(Order = 10)]
public sealed class ValidationBehavior : IPipelineBehavior { ... }

[PipelineBehavior(Order = 20)]
public sealed class CachingBehavior : IPipelineBehavior { ... }
```

**Convention:** Use multiples of 10 (0, 10, 20, 30...) so you can insert behaviors between existing ones without renumbering.

## ZAM008 — Handler Has No Parameterless Constructor

**Severity:** Warning

**What it means:** A handler class has only parameterised constructors. The static
`Mediator.Send/Publish/CreateStream` dispatch will throw `InvalidOperationException`
at runtime unless you register a factory with `Mediator.Configure(...)` or
register the handler in DI via
`services.AddMediator().RegisterHandlersFromAssembly(...)` and inject `IMediator`.

**Example that triggers it:**
```csharp
public class GetProductHandler : IRequestHandler<GetProductQuery, ProductDto>
{
    private readonly IProductRepository _repo;
    public GetProductHandler(IProductRepository repo) => _repo = repo; // ❌ no parameterless ctor
    public ValueTask<ProductDto> Handle(GetProductQuery q, CancellationToken ct) => ...;
}
```

**Fix options:**

1. **Inject `IMediator` (recommended for ASP.NET / hosted apps).** Add
   `services.AddMediator().RegisterHandlersFromAssembly(typeof(Program).Assembly);`
   at startup and inject `IMediator` instead of using the static `Mediator` class.
2. **Add a parameterless constructor.** Suitable for stateless handlers.
3. **Register a factory.** Call `Mediator.Configure(c => c.SetFactory<MyHandler>(...))`
   at startup if you must keep using the static API with a constructor-injected handler.
4. **Suppress.** `#pragma warning disable ZAM008` on the handler class if you
   know you only ever go through DI.

## Removed diagnostics

ZAM004 and ZAM007 were declared by the generator but never reported, and 6.0 removes them. A handler whose `Handle` method does not match its interface does not compile, so the C# compiler already catches both cases:

| Code | Was | What reports it instead |
|------|-----|-------------------------|
| ZAM004 | Invalid handler signature | `CS0535` or `CS0738`: the class does not implement the interface's `Handle` member |
| ZAM007 | Stream handler wrong return type | `CS0738`: a `Handle` that does not return `IAsyncEnumerable<TResponse>` does not implement `IStreamRequestHandler<TRequest, TResponse>` |

The IDs are retired, not reused. A `#pragma warning disable` or `NoWarn` entry for either ID never had any effect and can be deleted. See [Migrating to 6.0](migrating-to-v6.md).

## Where each diagnostic is reported

Every diagnostic points at the code it is about, so the IDE can take you there and a `#pragma` around that code suppresses that one case:

| Code | Reported at |
|------|-------------|
| ZAM001 | The request type's name |
| ZAM002 | The later handler's class name, with the other handlers as additional locations |
| ZAM003 | The request type's name, or the handler's class name when the request type is declared in another assembly |
| ZAM005 | The behavior's class name |
| ZAM006 | The later `[PipelineBehavior]` attribute, with the other tied behaviors in this project as additional locations. A tie between behaviors from referenced assemblies only has no location in your code. |
| ZAM008 | The handler's class name |

## Suppressing Warnings

If you intentionally use a class request type (ZAM003) or have duplicate Order values (ZAM006) for a valid reason, suppress with `#pragma` around the location in the table above:

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

## Release tracking

`src/ZeroAlloc.Mediator.Generator/AnalyzerReleases.Shipped.md` records the release each diagnostic first shipped in, any later change to its category or severity, and each removal, such as ZAM004 and ZAM007 in 6.0.0. A new diagnostic goes into `AnalyzerReleases.Unshipped.md`. Changing a shipped diagnostic's severity or category, or removing it, has to be declared there under `### Changed Rules` or `### Removed Rules`, or the build fails. The same move covers every `PublicAPI.Unshipped.txt`: new public API goes there, and removing shipped API is declared with a `*REMOVED*` line.

Nobody moves entries by hand. When release-please opens or updates the release PR, the `ship-release-tracking` job in `.github/workflows/release-please.yml` moves everything unshipped into the Shipped files on that branch, in a `chore: mark analyzer rules and public api shipped in <version>` commit. The `release-tracking` job in CI fails a release PR while anything is still unshipped. Both use the shared [`ship-release-tracking.py`](https://github.com/ZeroAlloc-Net/.github/blob/main/scripts/ship-release-tracking.py). **Before merging a release PR,** check that it has that commit. If it doesn't, run the script with the release version from the root of the release branch and push the result.
