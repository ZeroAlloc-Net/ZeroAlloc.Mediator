# Authorization

`ZeroAlloc.Mediator.Authorization` is a sub-package that gates Mediator dispatch on `[RequirePolicy]` policy checks. It bridges the [`ZeroAlloc.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) v2 contract package into the Mediator pipeline via a single `IPipelineBehavior`. The compile-time policy/request lookup is generator-emitted from `ZeroAlloc.Authorization` itself — this host now ships only the pipeline behavior + DI builder.

```bash
dotnet add package ZeroAlloc.Mediator.Authorization
```

> **Requires `ZeroAlloc.Authorization` >= 2.0.0.** Mediator core stays on the 4.x line; `Mediator.Authorization` versions independently starting at 2.0.0.

## Quick start

### 1. Define a policy

```csharp
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Abstractions;
using CSharpFunctionalExtensions;

[Policy("admin")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}
```

Policies that complete synchronously wrap the result in `new ValueTask<UnitResult<AuthorizationFailure>>(syncResult)` — no allocation. Async policies (e.g. DB-backed permission lookups) return a real `ValueTask` from the async machinery.

### 2. Decorate a request

```csharp
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;

[RequirePolicy("admin")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;
```

### 3. Wire up

The setup is **two calls**, in this order:

```csharp
services.AddZeroAllocAuthorization();   // contract-side registry (generator-emitted)

services.AddMediator(b => b.WithAuthorization(auth =>
{
    // Pick ONE security-context source:
    auth.UseSecurityContextFactory(sp => /* derive from HttpContext.User, etc. */);
    // auth.UseAnonymousSecurityContext();         // testing / no-auth
    // auth.UseAccessor<MySecurityContextAccessor>();
}));
```

`AddZeroAllocAuthorization()` registers the policy lookup (a single generator-emitted dictionary) and every `[Policy]`-decorated class as scoped. `WithAuthorization(...)` then plugs the Mediator pipeline behavior into that registry.

If you forget the first call — or invoke it *after* `AddMediator(...)` — the D3 startup guard throws:

```text
System.InvalidOperationException: ZeroAlloc.Mediator.Authorization requires
services.AddZeroAllocAuthorization() to be called BEFORE
services.AddMediator(...).WithAuthorization(...).
```

There is no longer an `AutoRegisterDiscoveredPolicies` or `ValidatePoliciesAreRegistered` option — the contract package owns registration, and the guard owns the "did you forget?" check.

## Throw vs Result deny path

You pick **per request** how denial surfaces:

### Throw path (default)

```csharp
[RequirePolicy("admin")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;

// Caller:
await mediator.Send(new DeleteUserCommand("alice"), ct);  // throws AuthorizationDeniedException on deny
```

### Result path (type-safe)

Replace `IRequest<T>` with `IAuthorizedRequest<T>`. The marker interface refines the response type to `Result<T, AuthorizationFailure>`:

```csharp
using ZeroAlloc.Mediator.Authorization;

[RequirePolicy("admin")]
public sealed record DeleteUserCommand(string UserId) : IAuthorizedRequest<Unit>;

// Caller:
Result<Unit, AuthorizationFailure> result = await mediator.Send(new DeleteUserCommand("alice"), ct);
if (result.IsFailure) return Forbid(result.Error.Code);
```

The handler still returns plain `T` — the wrap is symmetric, hidden in the behavior.

## Multiple policies (AND)

Stacking `[RequirePolicy]` attributes is implicit AND with short-circuit on first deny:

```csharp
[RequirePolicy("admin")]
[RequirePolicy("premium")]
public sealed record ExportUsersCommand : IRequest<byte[]>;
```

Both policies must pass. Evaluation order matches source order. OR mode depends on a future `Mode` parameter on the contract's `[RequirePolicy]` attribute (see Authorization backlog #1).

## Pipeline ordering

The `AuthorizationBehavior` registers at `[PipelineBehavior(Order = -1000)]` — runs early, before logging/validation/caching. To run another behavior before authz, give it a smaller order:

```csharp
[PipelineBehavior(Order = -2000)]
public sealed class CorrelationIdBehavior : IPipelineBehavior { ... }
```

## Diagnostics

The compile-time diagnostics are emitted by the generator that now lives in `ZeroAlloc.Authorization` v2 (no longer bundled with this host). They are prefixed `ZAUTH`:

| ID | Severity | Meaning |
|---|---|---|
| `ZAUTH001` | Error | `[RequirePolicy("X")]` references a policy with no matching `[Policy("X")]` |
| `ZAUTH002` | Error | Two `[Policy]` declarations share the same name |
| `ZAUTH003` | Warning | `IAuthorizedRequest<T>` declared without any `[RequirePolicy]` attribute |
| `ZAUTH004` | Error | `[RequirePolicy]` on a non-`IRequest`/non-`IAuthorizedRequest` type |
| `ZAUTH005` | Error | `[RequirePolicy]` on an `INotification` type — not supported |

See the [`ZeroAlloc.Authorization` diagnostics reference](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization#diagnostics) for the canonical list.

## Versioning

`ZeroAlloc.Mediator.Authorization` versions **independently** of Mediator core starting at 2.0.0. Compatibility matrix:

| `Mediator.Authorization` | Requires `ZeroAlloc.Authorization` | Mediator family | Notes |
|---|---|---|---|
| 2.0.x | ≥ 2.0.0 | 4.x | New baseline. `[Policy]` / `[RequirePolicy]`, async-only contract, two-call DI. |
| 2.1.x | ≥ 2.1.0 (with `Mode` support) | 4.x | Adds OR via stacked `[RequirePolicy(Mode = Any)]` |
| 2.2.x | ≥ 2.2.0 (with parameterized policies) | 4.x | `[RequirePolicy("MinAge", 18)]` |
| 3.0.x | ≥ 3.0.0 | 4.x or 5.x | Major if contract majors or host runtime surface breaks |

When `ZeroAlloc.Authorization` ships new contract features, the host falls into one of three buckets:

- **Transparent** — additive contract changes (new method with default-interface impl, new property with default value). No host work needed.
- **Generator update required** — new attribute properties affecting emission shape (e.g. `Mode`, `[RequirePolicy("MinAge", 18)]`). Without the host update, the generator silently emits the older shape; mitigated by `ZAUTH`-prefixed diagnostics on the contract side.
- **Runtime + DI surface change required** — new resolution shape (e.g. `IResourceSecurityContext<TRequest>`) or breaking failure-shape changes. Major version bump of the host.

See [`docs/plans/2026-05-06-mediator-authorization-design.md`](plans/2026-05-06-mediator-authorization-design.md) for the full bucket-by-feature matrix.

> **Migrating from v1.x?** v1 used `[AuthorizationPolicy]` + `[Authorize]` and a 4-method synchronous `IAuthorizationPolicy`; v2 uses `[Policy]` + `[RequirePolicy]` and a single async `EvaluateAsync` method. v1 also lockstepped with Mediator core. See the [Authorization v2 migration notes](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/MIGRATION-v2.md).

## AOT publish

`AuthorizationBehavior` resolves the deny-path `Result<TPayload, AuthorizationFailure>.Failure(...)` constructor reflectively via a per-`TResponse` cache (`AuthorizationFailureFactory<TResponse>`). The reflection is **trim-fragile**: under `PublishAot=true`, the .NET trimmer strips closed-generic methods that aren't statically referenced from anywhere in your code. Handlers typically use the implicit `TPayload → Result<TPayload, AuthorizationFailure>` conversion for the success path and never call `.Failure(...)` directly, so the trimmer removes `Result<TPayload, AuthorizationFailure>.Failure(AuthorizationFailure)` from your published binary. At runtime the factory's `GetMethod` lookup returns `null`, the dispatcher falls through to the throw path, and your `IAuthorizedRequest<TPayload>` request that should have returned `Result.Failure` instead throws `AuthorizationDeniedException`.

**Symptom under AOT:** `IAuthorizedRequest<T>` deny throws `AuthorizationDeniedException` (the `IRequest<T>` throw-path shape) instead of returning `Result<T, AuthorizationFailure>.Failure(...)`. The non-AOT (JIT) build returns the correct shape because the JIT loads methods lazily and the reflection lookup succeeds.

**Fix:** annotate a reachable method in your assembly with `[DynamicDependency]` so the trimmer preserves the closed-type method for each `TPayload` you use with `IAuthorizedRequest<TPayload>`. A `[ModuleInitializer]` carrier method is the cleanest spot — it runs once at module load (no runtime cost) and the attributes are reachable from the entry point:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

internal static class AotTrimPreservation
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Result<int, AuthorizationFailure>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Result<string, AuthorizationFailure>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Result<MyDto, AuthorizationFailure>))]
    // ...one [DynamicDependency] per closed Result<TPayload, AuthorizationFailure> you use
    [ModuleInitializer]
    internal static void PreserveAuthorizationFailureFactories() { }
}
```

You only need entries for `TPayload`s actually used with `IAuthorizedRequest<TPayload>` in your app. Plain `IRequest<T>` requests (throw path) don't need this annotation — the throw path never touches the `Result<,>` reflection.

**Why this isn't generator-emitted:** the v2.0 architecture deliberately deletes the in-package generator and consolidates code generation into `ZeroAlloc.Authorization`. Adding a Mediator-specific trim-hint generator here would reintroduce the generator project we just removed and couple `ZeroAlloc.Authorization`'s generator to a Mediator-shaped contract. Documented consumer-side annotation is the same pattern Microsoft uses for `Microsoft.Extensions.DependencyInjection`'s open-generic registration paths under AOT — it keeps the library trim-honest and the layering clean.

If `Result<TPayload, AuthorizationFailure>`-shaped responses become a primary v3 use case, a self-referential static-virtual interface (`IAuthorizedRequest<TSelf, TPayload>` with a `static abstract CreateFailure`) eliminates the reflection entirely and removes the need for these annotations. Tracked as a v3 design consideration.

## See also

- [`ZeroAlloc.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) — the contract package this host adapts (also ships the source generator).
- [Pipeline Behaviors](pipeline-behaviors.md) — how authz fits with logging, validation, caching.
- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — the other shipping host of the same contract.
