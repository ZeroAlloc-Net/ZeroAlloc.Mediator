# Authorization

`ZeroAlloc.Mediator.Authorization` is a sub-package that gates Mediator dispatch on `[Authorize]` policy checks. It bridges the [`ZeroAlloc.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) contract package into the Mediator pipeline via a single source-generated lookup and one `IPipelineBehavior`.

```bash
dotnet add package ZeroAlloc.Mediator.Authorization
```

## Quick start

### 1. Define a policy

```csharp
using ZeroAlloc.Authorization;

[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}
```

### 2. Decorate a request

```csharp
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;

[Authorize("AdminOnly")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;
```

### 3. Wire up

```csharp
services.AddMediator()
    .WithAuthorization(opts =>
    {
        // Pick ONE security-context source:
        opts.UseSecurityContextFactory(sp => /* derive from HttpContext.User, etc. */);
        // opts.UseAnonymousSecurityContext();         // testing / no-auth
        // opts.UseAccessor<MyContextAccessor>();      // for users with their own indirection
    });
```

That's it. The generator scans your compilation, discovers all `[AuthorizationPolicy]` types and `[Authorize]`-decorated requests, emits a compile-time lookup, and auto-registers each policy class in DI as scoped.

## Throw vs Result deny path

You pick **per request** how denial surfaces:

### Throw path (default)

```csharp
[Authorize("AdminOnly")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;

// Caller:
await mediator.Send(new DeleteUserCommand("alice"), ct);  // throws AuthorizationDeniedException on deny
```

### Result path (type-safe)

Replace `IRequest<T>` with `IAuthorizedRequest<T>`. The marker interface refines the response type to `Result<T, AuthorizationFailure>`:

```csharp
using ZeroAlloc.Mediator.Authorization;

[Authorize("AdminOnly")]
public sealed record DeleteUserCommand(string UserId) : IAuthorizedRequest<Unit>;

// Caller:
Result<Unit, AuthorizationFailure> result = await mediator.Send(new DeleteUserCommand("alice"), ct);
if (result.IsFailure) return Forbid(result.Error.Code);
```

The handler still returns plain `T` — the wrap is symmetric, hidden in the generator-emitted behavior.

## Multiple policies (AND)

Stacking `[Authorize]` attributes is implicit AND with short-circuit on first deny:

```csharp
[Authorize("Admin")]
[Authorize("Premium")]
public sealed record ExportUsersCommand : IRequest<byte[]>;
```

Both policies must pass. Evaluation order matches source order. **OR mode is not supported in v1**; it depends on a future `Mode` parameter on the contract's `[Authorize]` attribute (see Authorization backlog #1).

## Pipeline ordering

The generated `AuthorizationBehavior` registers at `[PipelineBehavior(Order = -1000)]` — runs early, before logging/validation/caching. To run another behavior before authz, give it a smaller order:

```csharp
[PipelineBehavior(Order = -2000)]
public sealed class CorrelationIdBehavior : IPipelineBehavior { ... }
```

## Diagnostics

The generator emits compile-time diagnostics:

| ID | Severity | Meaning |
|---|---|---|
| `ZAMA001` | Error | `[Authorize("X")]` references a policy with no matching `[AuthorizationPolicy("X")]` |
| `ZAMA002` | Error | Two `[AuthorizationPolicy]` declarations share the same name |
| `ZAMA003` | Warning | `IAuthorizedRequest<T>` declared without any `[Authorize]` attribute |
| `ZAMA004` | Error | `[Authorize]` on a non-`IRequest`/non-`IAuthorizedRequest` type |
| `ZAMA005` | Error | Future contract attribute property detected (e.g. `Mode`) on an older host version |
| `ZAMA006` | Error | `[Authorize]` on an `INotification` type — not supported in v1 |

## Tracking the contract

`ZeroAlloc.Mediator.Authorization` versions in lockstep with the rest of the Mediator family. Compatibility matrix:

| `Mediator.Authorization` | Requires `ZeroAlloc.Authorization` | Mediator family | Notes |
|---|---|---|---|
| 4.1.x | ≥ 1.1.0 | 4.x | Baseline |
| 4.2.x | ≥ 1.2.0 (with `Mode` support) | 4.x | Adds OR via stacked + `Mode = Any` |
| 4.3.x | ≥ 1.3.0 (with parameterized policies) | 4.x | `[Authorize("MinAge", 18)]` |
| 5.0.x | ≥ 2.0.0 | 5.x | Major if either dep majors |

When `ZeroAlloc.Authorization` ships new contract features, the host falls into one of three buckets:

- **Transparent** — additive contract changes (new method with default-interface impl, new property with default value). No host work needed.
- **Generator update required** — new attribute properties affecting emission shape (e.g. `Mode`, `[Authorize("MinAge", 18)]`). Without the host update, the generator silently emits the older shape; mitigated by `ZAMA005`.
- **Runtime + DI surface change required** — new resolution shape (e.g. `IResourceSecurityContext<TRequest>`) or breaking failure-shape changes. Major version bump of the host.

See [`docs/plans/2026-05-06-mediator-authorization-design.md`](plans/2026-05-06-mediator-authorization-design.md) for the full bucket-by-feature matrix.

## See also

- [`ZeroAlloc.Authorization`](https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization) — the contract package this host adapts.
- [Pipeline Behaviors](pipeline-behaviors.md) — how authz fits with logging, validation, caching.
- [AI.Sentinel](https://github.com/MarcelRoozekrans/AI.Sentinel) — the other shipping host of the same contract.
