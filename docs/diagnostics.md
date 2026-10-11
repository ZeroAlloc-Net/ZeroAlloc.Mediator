---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZAM001–ZAM011 Roslyn analyzer rules with triggers, severities, and fix guidance.
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
| ZAM009 | Warning | Pipeline behavior does not implement IPipelineBehavior | A `[PipelineBehavior]` class is static or does not implement `IPipelineBehavior`, so it never runs |
| ZAM010 | Error | Cached request type has no per-instance cache key | A `[CacheResponse]` request is a class or struct with instance state whose text is its type name, so all its instances share one cache entry |
| ZAM011 | Error | Cached record request has a member that prints as its type name | A `[CacheResponse]` record prints a member, such as an array or `List<T>`, as its type name, so requests that differ only in it share one cache entry |

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

ZAM005 is only reported for a class the generator picked up as a behavior, which requires `IPipelineBehavior`. A `static class`, or a class without `IPipelineBehavior`, is left out of the pipeline and reported as [ZAM009](#zam009--pipeline-behavior-does-not-implement-ipipelinebehavior) instead.

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
`services.AddMediator()` and inject `IMediator`.

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
   `services.AddMediator();`
   at startup and inject `IMediator` instead of using the static `Mediator` class.
2. **Add a parameterless constructor.** Suitable for stateless handlers.
3. **Register a factory.** Call `Mediator.Configure(c => c.SetFactory<MyHandler>(...))`
   at startup if you must keep using the static API with a constructor-injected handler.
4. **Suppress.** `#pragma warning disable ZAM008` on the handler class if you
   know you only ever go through DI.

## ZAM009 — Pipeline Behavior Does Not Implement IPipelineBehavior

**Severity:** Warning

**What it means:** A class marked `[PipelineBehavior]` does not implement `IPipelineBehavior`. The generator only puts a class that implements it into the pipeline, so the generated `Send` has no call to this behavior and it never runs. The common case is a `static class`, which cannot implement an interface at all.

**Example that triggers it:**
```csharp
// ❌ Triggers ZAM009 — a static class cannot implement IPipelineBehavior
[PipelineBehavior(Order = 0)]
public static class LoggingBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    {
        Console.WriteLine($"[START] {typeof(TRequest).Name}");
        return await next(request, ct);
    }
}

// ❌ Triggers ZAM009 — the class does not implement IPipelineBehavior
[PipelineBehavior(Order = 10)]
public sealed class ValidationBehavior { ... }
```

**Warning message:** for the static class, `ZAM009: Pipeline behavior 'LoggingBehavior' does not implement IPipelineBehavior and never runs; a static class cannot implement an interface, so make the class non-static and implement IPipelineBehavior`. For the other class, the message ends in `implement IPipelineBehavior`.

**Fix:** Make the class non-static and implement `IPipelineBehavior`. Only `Handle` is static:
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
        return await next(request, ct);
    }
}
```

ZAM009 is a warning rather than an error because such a class compiled without complaint before it was added. The class still stays out of the pipeline; ZAM005 and ZAM006 are not reported for it.

## ZAM010 — Cached Request Type Has No Per-Instance Cache Key

**Severity:** Error

**What it means:** `CacheBehavior` from `ZeroAlloc.Mediator.Cache` stores a response under the key `"<request type full name>:<request>"`, where the request part is the request's text: its `IFormattable` or `ISpanFormattable` formatting when it implements one, its `ToString()` otherwise. A class or struct that is not a record, does not override `ToString()` and does not implement `IFormattable` formats as its type name. Every instance then has the same key, so the response to the first request is cached and returned for every later request of that type, whatever its values.

**Example that triggers it:**
```csharp
// ❌ Triggers ZAM010 — every GetUser formats as "TestApp.GetUser", whatever its Id
[CacheResponse(TtlMs = 60_000)]
public sealed class GetUser : IRequest<User>
{
    public int Id { get; init; }
}
```

**Error message:** `ZAM010: Request type 'global::TestApp.GetUser' has [CacheResponse], but its cache key is its type name, so every instance shares one cache entry and gets the first response; make it a record, override ToString, or implement ISpanFormattable so the key identifies the request`

**Fix:** give the request a text that identifies it. Any one of these does:

```csharp
// ✅ A record: the compiler-written ToString prints every member
[CacheResponse(TtlMs = 60_000)]
public readonly record struct GetUser(int Id) : IRequest<User>;

// ✅ A ToString override, on the type or a base class
[CacheResponse(TtlMs = 60_000)]
public sealed class GetUser : IRequest<User>
{
    public int Id { get; init; }
    public override string ToString() => Id.ToString(CultureInfo.InvariantCulture);
}

// ✅ ISpanFormattable, which the key interpolation uses instead of ToString, and which also
// makes a cache hit allocation-free. Implementing IFormattable alone works too.
[CacheResponse(TtlMs = 60_000)]
public readonly struct GetUser : IRequest<User>, ISpanFormattable
{
    public GetUser(int id) => Id = id;
    public int Id { get; }
    public override string ToString() => Id.ToString(CultureInfo.InvariantCulture);
    public string ToString(string? format, IFormatProvider? provider) => Id.ToString(format, provider);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Id.TryFormat(destination, out charsWritten, format, provider);
}
```

**Not reported:**
- A type without instance state, such as `public sealed class GetAllCountries : IRequest<Country[]> { }`. All its instances are the same request, so one cache entry is right. Instance state is an instance field, an auto-implemented property, a property that uses the `field` keyword, or a primary constructor parameter, on the type or a base class. A property of a base class from another assembly counts as state, because the generator cannot see whether it has a backing field.
- A type without `[CacheResponse]`, and a `[CacheResponse]` type that does not implement `IRequest<TResponse>`.

ZAM010 checks the request type, not its members, and does not report records. A record member that prints as its type name is [ZAM011](#zam011--cached-record-request-has-a-member-that-prints-as-its-type-name).

ZAM010 is an error, not a warning, because the behavior it reports is always wrong: the cache answers requests with another request's response. A build that compiled before ZAM010 can fail with it; that build was returning wrong cached responses.

## ZAM011 — Cached Record Request Has a Member That Prints as Its Type Name

**Severity:** Error

**What it means:** a record's compiler-written `ToString()` prints each member through the member's own `ToString()`. A member whose type keeps `object.ToString()`, such as an array or a `List<T>`, prints as its type name, for example `System.Int32[]`. `CacheBehavior` keys an entry by the request's text, so two requests that differ only in that member get the same key, and the second gets the first one's response.

**Example that triggers it:**
```csharp
// ❌ Triggers ZAM011 at Ids — GetUsers([1]) and GetUsers([2]) both key as
// "TestApp.GetUsers:GetUsers { Ids = System.Int32[] }"
[CacheResponse(TtlMs = 60_000)]
public readonly record struct GetUsers(int[] Ids) : IRequest<User[]>;
```

**Error message:** `ZAM011: Member 'Ids' of [CacheResponse] request 'global::TestApp.GetUsers' has type 'int[]', which the record's ToString prints as its type name, so requests that differ only in it share one cache entry and get the first response; override ToString or PrintMembers on the request to format it, or use a type whose ToString prints its value`

**Fix:** make the member print its value. Either format it on the request, or give it a type that does:

```csharp
// ✅ PrintMembers on the request: the compiler-written ToString still adds the type name and braces
[CacheResponse(TtlMs = 60_000)]
public readonly record struct GetUsers(int[] Ids) : IRequest<User[]>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Ids = ").AppendJoin(',', Ids);
        return true;
    }
}

// ✅ A member type whose ToString prints its value, such as a record of scalars or a type
// with its own ToString override
public readonly record struct UserRange(int From, int To);

[CacheResponse(TtlMs = 60_000)]
public readonly record struct GetUsers(UserRange Range) : IRequest<User[]>;
```

A `ToString()` override on the request works too. A sealed record or a record struct declares `PrintMembers` as `private`; a non-sealed record class declares it `protected virtual`.

**Which members are checked:** the ones the compiler-written `PrintMembers` prints: public instance fields, and public instance properties with a getter that are not indexers or overrides, including those of base records. A base record that declares its own `PrintMembers` prints its members, and its bases' members, its own way, so they are not checked.

**Which member types are reported:**
- arrays;
- interfaces that extend `IEnumerable`, such as `IReadOnlyList<T>`;
- classes and structs that are not records and have no `ToString()` override on themselves or on a base class other than `object` or `ValueType`. This includes collections such as `List<T>` and `Dictionary<TKey, TValue>`, and delegates;
- `Memory<T>` and `ReadOnlyMemory<T>` when `T` is not `char`: their `ToString()` prints only the length.

A member of a record type is checked member by member with the same rule, and reported at the outer member with the path to the inner one, such as `Filter.Tags`. A record that refers to itself is checked once. `Nullable<T>` is checked as `T`, and a tuple or a `KeyValuePair<TKey, TValue>` element by element.

**Not reported:**
- primitives, `string`, enums, and any type that overrides `ToString()`, which includes every `IFormattable` type in the BCL, such as `Guid`, `DateTime` and `decimal`;
- a record request that declares `ToString()` or `PrintMembers`, inherits a sealed `ToString()` from a base record, or implements `IFormattable`, which the key interpolation uses instead of `ToString()`;
- a member typed as `object`, `dynamic`, or an interface that does not extend `IEnumerable`: its text depends on the runtime type;
- a member typed as a type parameter, whatever its constraints. No constraint makes it certain to print as a type name: a class constraint admits a derived class that overrides `ToString()`, and an interface constraint admits a type that does. A generic type built from it, such as `List<T>`, is still reported;
- non-public, static, write-only and indexer members, which `PrintMembers` does not print.

Implementing `IFormattable` does not exempt a **member** type: `PrintMembers` appends a member with `ToString()`, not through `IFormattable`, so a struct that implements `IFormattable` without overriding `ToString()` still prints as its type name and is reported.

ZAM011 is an error for the same reason as ZAM010: requests that differ only in such a member get another request's cached response. A build that compiled before ZAM011 can fail with it; that build was returning wrong cached responses. If every request is known to hold the same value in such a member, suppress it with `#pragma` around the member.

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
| ZAM009 | The behavior's class name |
| ZAM010 | The request type's name, in the declaration that carries `[CacheResponse]` |
| ZAM011 | The member's name: the positional parameter, property or field, which may be in a base record. A member of a base record from another assembly is reported at the request type's name |

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
