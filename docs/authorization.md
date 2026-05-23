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

## Resource-based policies

When a policy needs to inspect the dispatched request itself (not just the caller's identity), type-check the security context for `IResourceSecurityContext<TRequest>`. `Mediator.Authorization` wraps the resolved `ISecurityContext` in a per-dispatch adapter so the cast resolves automatically — no host configuration required.

```csharp
[Policy("OwnerOnlyDelete")]
public sealed class OwnerOnlyDeletePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<DeleteUserCommand> rc
                && string.Equals(rc.Resource.UserId, ctx.Id, StringComparison.Ordinal)
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("delete.not_owner"));
}

[RequirePolicy("OwnerOnlyDelete")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;
```

The resource exposed to the policy IS the dispatched request. To access a sub-property (e.g. `request.Order.OwnerId`), pull it inside the policy body via `rc.Resource.Order.OwnerId` — no marker interface, no extractor delegate.

Policies that don't need the resource ignore the adapter; the `ISecurityContext` members (`Id`, `Roles`, `Claims`) forward through. The per-dispatch cost is one ~16 B class allocation, absorbed by the existing `AuthorizationBehavior.Handle` allocation budget.

See [`ZeroAlloc.Authorization`'s `resource-based-authorization.md`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/blob/main/docs/core-concepts/resource-based-authorization.md) for the underlying contract and the design discussion behind it.

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

## See also

- [`ZeroAlloc.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) — the contract package this host adapts (also ships the source generator).
- [Pipeline Behaviors](pipeline-behaviors.md) — how authz fits with logging, validation, caching.
- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — the other shipping host of the same contract.
