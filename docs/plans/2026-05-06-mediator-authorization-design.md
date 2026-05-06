# ZeroAlloc.Mediator.Authorization — Design

**Date:** 2026-05-06
**Status:** Designed, ready for implementation plan
**Context:** First non-AI.Sentinel host of the `ZeroAlloc.Authorization` contract package. The AOT certification of that contract (Authorization backlog #6) was the gating prerequisite — it just shipped, so this design unblocks. Lives as a 6th sub-package in this repo (alongside `Mediator.Cache`, `Mediator.Resilience`, `Mediator.Telemetry`, `Mediator.Validation`).

## Problem

`ZeroAlloc.Authorization` ships contract types — `ISecurityContext`, `IAuthorizationPolicy`, `[Authorize]`, `[AuthorizationPolicy]`, `AuthorizationFailure` — but no host. Today the contract has exactly one shipping host: `AI.Sentinel`, for tool-call authorization on `IChatClient`-based agents.

The org README and the Authorization repo's own backlog call out a planned second host: `ZeroAlloc.Mediator.Authorization`, which adapts the same contract for **request-handler authorization** in `ZeroAlloc.Mediator` pipelines. Without it, Mediator users have no way to attach `[Authorize]` to a request and have it actually do anything — and Authorization backlog items #1, #3, #5 are blocked on a second host surfacing real friction.

## Scope

A new sub-package `ZeroAlloc.Mediator.Authorization` inside this repo that:

1. Lets a Mediator user attach `[Authorize("PolicyName")]` to an `IRequest<T>` (or new `IAuthorizedRequest<T>`) and have the request's dispatch automatically gated on the policy passing.
2. Does it via a single hand-coded `IPipelineBehavior<TRequest, TResponse>` that consumes a compile-time-emitted lookup of policies-per-request — same shape as `ZeroAlloc.Mediator.Validation` (one runtime behavior class, generic dispatch over `TRequest`).
3. Stays **contract-faithful** — no host-specific extensions to `[Authorize]` or `IAuthorizationPolicy` syntax. Anything new (AND/OR mode, parameterized policies, resource-based context) belongs in the contract first; this host adapts when the contract ships them.

Targets `net8.0`, `net10.0` — same TFM matrix as the rest of the Mediator family.

## Architecture

Two layers, matching the family pattern already established by `Mediator.Cache`/`Resilience`/`Telemetry`/`Validation`:

1. **Runtime** (~120 LOC, hand-coded, in `src/ZeroAlloc.Mediator.Authorization/`):
   - `IAuthorizedRequest<TResponse>` — marker interface for the Result-shaped deny path.
   - `AuthorizationDeniedException` — thrown on the throw path.
   - `AuthorizationOptions` + `WithAuthorization(opts => ...)` builder extension.
   - `ISecurityContextAccessor` — optional indirection.
   - **`AuthorizationBehavior<TRequest, TResponse>` — the one and only pipeline behavior.** Resolves `ISecurityContext`, looks up the policies for `TRequest` via the generator-emitted `GeneratedAuthorizationLookup.GetPoliciesFor<TRequest>()`, calls each policy's `EvaluateAsync`, throws or returns Result on deny.
   - `MediatorAuthorizationServiceCollectionExtensions` — DI builder.

2. **Generator** (Roslyn `IIncrementalGenerator`, in `src/ZeroAlloc.Mediator.Authorization.Generator/`):
   - Scans the user's compilation for `[AuthorizationPolicy]`-decorated types and `[Authorize]`-decorated request types.
   - Emits **one** static class per compilation: `GeneratedAuthorizationLookup` with two entry points:
     - `GetPoliciesFor<TRequest>()` → `string[]` policy names (compile-time switch on `typeof(TRequest)`).
     - `Resolve(string name, IServiceProvider sp)` → `IAuthorizationPolicy` (compile-time switch on policy name).
     - `PolicyTypes` → `Type[]` for eager DI validation at startup.
   - Lives in a separate generator project so it can be deleted / replaced when Authorization backlog #5 (source-generated policy registry inside the contract package) graduates and ships.

Three external dependencies (added to the new sub-package csproj):

- `ZeroAlloc.Authorization` — contract types (floor `1.1.0+`, the version that just landed AOT certification).
- `ZeroAlloc.Results` — `Result<T, E>` for the type-safe deny path.
- (Project reference) `ZeroAlloc.Mediator` — `IPipelineBehavior`, `IRequest<>`, etc.

No `Microsoft.AspNetCore.*` and no logging library.

The runtime behavior registers at `[PipelineBehavior(Order = -1000)]` by default — early in the pipeline, before logging/validation/cache.

## Foundational design decisions

Each was an explicit branch point during brainstorming. Recording rationale so future maintainers don't re-litigate.

### 1. Security-context source: DI as `ISecurityContext`

**Decision:** the host resolves `ISecurityContext` from DI directly. User registers it scoped (typically derived from `IHttpContextAccessor` in ASP.NET Core, or a custom worker context). `WithAuthorization()` provides convenience helpers (`UseSecurityContextFactory`, `UseAnonymousSecurityContext`, `UseAccessor<>`) but the underlying registration is the user's responsibility.

**Rejected:** owning an `ISecurityContextAccessor` interface (extra indirection without value); requiring requests to carry context inline (viral, every call site has to populate).

**Why:** keeps the host dependency-light, lets the user's host own the wiring, matches AI.Sentinel's pattern.

### 2. Integration shape: single runtime behavior + per-request lookup table (option D)

**Decision:** **One** runtime class — `AuthorizationBehavior<TRequest, TResponse>` — handles all authorized requests. The source generator emits a single `GeneratedAuthorizationLookup` static class with a compile-time `switch` on `typeof(TRequest)` returning the policy names to evaluate. Per-request work is a row in the switch. No per-request behavior classes.

**Rejected:**

- Per-request `IPipelineBehavior` (option A — original brainstorming pick): more generated code, *not* the family pattern. The brainstorming argument that this was "the idiomatic Mediator extension shape" was wrong — looking at sibling sub-packages in this repo (Cache, Resilience, Telemetry, Validation), **none** of them generate per-request behaviors. They all use a single behavior that resolves per-request work via DI generic dispatch. We match that.
- Generator wraps the handler directly (bypasses pipeline): breaks composition with logging/validation/cache behaviors.

**Why D wins:** matches sibling packages exactly (one pattern across the family, easier maintenance). ~80% less generated code (one lookup class vs N behavior classes). Same zero-reflection / AOT-safe property — the switch on `typeof(TRequest)` is resolved at JIT/AOT compile time when generic `TRequest` is closed.

### 3. Failure semantics: dual-path via marker interface (option D from failure brainstorming)

**Decision:** the user picks per-request whether deny throws or returns a `Result`:

- `IRequest<T>` + `[Authorize]` → throws `AuthorizationDeniedException` on deny.
- `IAuthorizedRequest<T>` + `[Authorize]` → returns `Result<T, AuthorizationFailure>.Failure(...)` on deny.

`IAuthorizedRequest<TResponse> : IRequest<Result<TResponse, AuthorizationFailure>>` — the marker interface refines the response type. The runtime behavior inspects `TResponse` at compile time (via generic specialization) and emits the right code path.

**Why:** type-safety for users who want it, exception-based simplicity for users who don't, no composability problem because each request opts in independently.

### 4. Multi-policy semantics: implicit AND, defer OR

**Decision:** stacking `[Authorize]` attributes means AND. The behavior iterates the lookup-returned policy names sequentially, short-circuits on first deny. OR mode (`Mode = AuthorizeMode.Any`) is NOT supported in v1 — `Mode` belongs on the contract's `[Authorize]` (Authorization backlog #1), not duplicated here.

**Why:** AND is the dominant real-world case. Surfacing the "stacking is AND-only" friction is exactly the graduation signal Authorization backlog #1 is waiting for. Generator emits `ZAMA005` if it detects a future `Mode`-bearing attribute on an older host version ("requires ZeroAlloc.Authorization vX.Y.Z or later").

### 5. Generator location: host-side, not contract-side

**Decision:** the per-request lookup generator lives in *this* sub-package (`src/ZeroAlloc.Mediator.Authorization.Generator/`). The Authorization contract package itself is NOT modified.

**Rejected:** Path X — ship Authorization backlog #5 first (contract-side generator emitting `AuthorizerFor<TRequest>` per request), then have this host consume it via DI generic dispatch matching `Mediator.Validation`'s pattern exactly.

**Why Y over X:** Authorization backlog #5's stated graduation signal is *literally* "the second host (Mediator.Authorization) is being built and the author finds themselves writing scan-and-register code." #5 is supposed to ship AFTER feeling this friction, not before. Building host-side now ships value, surfaces the documented friction, and provides concrete evidence (the exact scan-and-register code) of what #5 should look like. When #5 graduates, ~80 LOC of this host's generator gets deleted and replaced by `AuthorizerFor<>` resolution — straightforward migration.

## Components

### Runtime layer (hand-coded, file inventory)

Mirrors `src/ZeroAlloc.Mediator.Validation/`'s file layout, with auth-specific names:

```
src/ZeroAlloc.Mediator.Authorization/
├── AuthorizationBehavior.cs              # The one IPipelineBehavior class
├── AuthorizationDeniedException.cs       # Throw-path exception
├── AuthorizationOptions.cs               # WithAuthorization(opts => ...) options
├── IAuthorizedRequest.cs                 # Marker interface for Result-path opt-in
├── ISecurityContextAccessor.cs           # Optional indirection
├── MediatorAuthorizationServiceCollectionExtensions.cs   # WithAuthorization() builder
├── PublicAPI.Shipped.txt
├── PublicAPI.Unshipped.txt
└── ZeroAlloc.Mediator.Authorization.csproj
```

### Key types (signatures)

```csharp
// Marker interface — opt into Result-shaped deny path.
public interface IAuthorizedRequest<TResponse>
    : IRequest<Result<TResponse, AuthorizationFailure>> { }

// Throw path.
public sealed class AuthorizationDeniedException : Exception
{
    public AuthorizationFailure Failure { get; }
    public AuthorizationDeniedException(AuthorizationFailure failure)
        : base($"Authorization denied: {failure.Code}") => Failure = failure;
}

// The single pipeline behavior.
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
{
    private readonly ISecurityContext _ctx;
    private readonly IServiceProvider _sp;

    public AuthorizationBehavior(ISecurityContext ctx, IServiceProvider sp)
    { _ctx = ctx; _sp = sp; }

    public async ValueTask<TResponse> Handle(
        TRequest request,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next,
        CancellationToken ct)
    {
        // Compile-time-resolved lookup. typeof(TRequest) is closed at the call site.
        var policies = GeneratedAuthorizationLookup.GetPoliciesFor<TRequest>();
        if (policies.Length == 0)
            return await next(request, ct).ConfigureAwait(false);

        for (int i = 0; i < policies.Length; i++)
        {
            var policy = GeneratedAuthorizationLookup.Resolve(policies[i], _sp);
            var result = await policy.EvaluateAsync(_ctx, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                // typeof(TResponse) inspection at JIT time chooses the path.
                if (typeof(TResponse).IsResultOfAuthorizationFailure())
                {
                    return (TResponse)(object)Result<object, AuthorizationFailure>.Failure(result.Error)!;
                }
                throw new AuthorizationDeniedException(result.Error);
            }
        }
        return await next(request, ct).ConfigureAwait(false);
    }
}
```

(The `typeof(TResponse).IsResultOfAuthorizationFailure()` branch is non-trivial to keep AOT-clean — actual implementation will use a compile-time-resolved sentinel via constrained generics or a separate behavior subclass per path. Detail to settle in implementation.)

### Builder + options

```csharp
public sealed class AuthorizationOptions
{
    public void UseSecurityContextFactory(Func<IServiceProvider, ISecurityContext> factory);
    public void UseAnonymousSecurityContext();
    public void UseAccessor<TAccessor>() where TAccessor : ISecurityContextAccessor;
}

public static class MediatorAuthorizationServiceCollectionExtensions
{
    public static IMediatorBuilder WithAuthorization(
        this IMediatorBuilder builder,
        Action<AuthorizationOptions>? configure = null);
}

public interface ISecurityContextAccessor
{
    ISecurityContext Current { get; }
}
```

### Generator output (in the user's compilation)

```csharp
// GeneratedAuthorizationLookup.g.cs
internal static class GeneratedAuthorizationLookup
{
    private static readonly string[] EmptyPolicies = Array.Empty<string>();

    public static string[] GetPoliciesFor<TRequest>() => typeof(TRequest) switch
    {
        var t when t == typeof(GetOrderById)     => new[] { "OrdersRead" },
        var t when t == typeof(DeleteUserCommand) => new[] { "Admin", "Premium" },  // stacked AND
        _ => EmptyPolicies,
    };

    public static IAuthorizationPolicy Resolve(string name, IServiceProvider sp) => name switch
    {
        "Admin"      => sp.GetRequiredService<AdminOnlyPolicy>(),
        "Premium"    => sp.GetRequiredService<PremiumPolicy>(),
        "OrdersRead" => sp.GetRequiredService<OrdersReadPolicy>(),
        _ => throw new InvalidOperationException($"Unknown policy '{name}'"),
    };

    public static Type[] PolicyTypes { get; } =
    {
        typeof(AdminOnlyPolicy),
        typeof(PremiumPolicy),
        typeof(OrdersReadPolicy),
    };
}
```

A second emitted file `GeneratedAuthorizationDIExtensions.g.cs` provides:
```csharp
internal static class GeneratedAuthorizationDIExtensions
{
    public static void RegisterDiscoveredPolicies(IServiceCollection services)
    {
        services.AddScoped<AdminOnlyPolicy>();
        services.AddScoped<PremiumPolicy>();
        services.AddScoped<OrdersReadPolicy>();
    }
}
```

`WithAuthorization()` calls `RegisterDiscoveredPolicies()` so the user doesn't have to register every policy manually. (User can opt out via `opts.AutoRegisterDiscoveredPolicies = false` if they want custom lifetimes.)

### Diagnostics

| ID | Severity | Trigger |
|---|---|---|
| `ZAMA001` | Error | `[Authorize("X")]` references a policy with no `[AuthorizationPolicy("X")]` declaration in compilation/references |
| `ZAMA002` | Error | Two `[AuthorizationPolicy]` declarations share the same name |
| `ZAMA003` | Warning | `IAuthorizedRequest<T>` declared without any `[Authorize]` attribute |
| `ZAMA004` | Error | `[Authorize]` on a non-`IRequest`/non-`IAuthorizedRequest` type |
| `ZAMA005` | Error | Future contract attribute property detected (e.g. `Mode`) on a host version that doesn't understand it |
| `ZAMA006` | Error | `[Authorize]` on an `INotification` type — not supported in v1 |

`ZAMA*` namespace is reserved exclusively for `Mediator.Authorization` so contract diagnostics (`ZA*`) don't collide.

## Data flow

```
1. Caller: await mediator.Send(new GetOrderById(42), ct);

2. Mediator's generated dispatcher resolves the pipeline. Behaviors run by Order:
     [Order=-1000]  AuthorizationBehavior<GetOrderById, OrderDto>  ← us (singleton class, generic-specialized at JIT)
     [Order=    0]  LoggingBehavior   (user-registered)
     [Order=  100]  ValidationBehavior (Mediator.Validation)
     [Order= 1000]  CachingBehavior   (Mediator.Cache)
     [Order=  ∞ ]  GetOrderByIdHandler

3. AuthorizationBehavior.Handle runs:
   a. ISecurityContext resolved on construction (per-scope DI).
   b. var policies = GeneratedAuthorizationLookup.GetPoliciesFor<GetOrderById>();
      → returns ["OrdersRead"] (or empty array if no [Authorize] on the type).
   c. For each name in policies (in declaration order):
       - var p = GeneratedAuthorizationLookup.Resolve(name, sp);
       - var r = await p.EvaluateAsync(_ctx, ct);
       - if r.IsFailure:
           - typeof(TResponse) is plain T:    throw new AuthorizationDeniedException(r.Error);
           - typeof(TResponse) is Result<,>:  return Result.Failure(r.Error);
       - else: continue.
   d. After all pass: return await next(request, ct);   // delegate to next behavior

4. Result<,> shaping (only for IAuthorizedRequest<T>):
   When all policies pass, the handler runs and returns its T (handler signature is
   IRequestHandler<Foo, Result<T, AuthFailure>> for Result-path requests — or the
   generator-emitted wrapper does the Result.Success(T) wrap. Implementation choice
   between "user writes handler returning Result" and "generator wraps" is settled
   in implementation; the user-facing API is the same either way: handler authors
   write the request type once, system handles the rest.).
```

**Cancellation** propagates via `EvaluateAsync(ctx, ct)`. `OperationCanceledException` propagates uncaught.

**Zero-allocation happy path:** policy resolution from DI, awaiting `EvaluateAsync` (synchronously-completed `ValueTask`), unwrapping `UnitResult.Success` (struct), calling `next()` — no boxing, no closures, no allocations. The `policies` array is pre-built static (never re-allocated). Allocation budget for the **deny** path is intentionally unconstrained.

## Error handling (edge cases beyond deny)

1. **`ISecurityContext` not registered.** `GetRequiredService<ISecurityContext>()` throws from MS.DI on first authorized dispatch. *Mitigation:* `WithAuthorization()` validates eagerly — if no `Use*` helper was called, throws at app boot.

2. **Policy class not registered in DI.** Same MS.DI failure shape. *Mitigation:* `WithAuthorization()` calls `GeneratedAuthorizationDIExtensions.RegisterDiscoveredPolicies()` automatically (opt-out via options). Eager startup walk verifies every `PolicyTypes` entry is registered.

3. **Policy throws.** Propagates uncaught. Authz does not swallow exceptions or convert them to deny.

4. **Cancellation mid-policy.** `OperationCanceledException` propagates uncaught. Cancellation ≠ deny.

5. **Compilation has `[Authorize]` but no `[AuthorizationPolicy]`.** Every `[Authorize]` reference fires `ZAMA001`. Build fails.

6. **`IAuthorizedRequest<T>` declared with no `[Authorize]`.** `ZAMA003` warning. Generator emits a passthrough behavior that just calls `next()`.

7. **Pipeline ordering collision.** User registers another behavior at `Order = -1000`. Mediator stable-sorts by Order then registration order. Document the default; recommend distinct orders.

## Testing

### `tests/ZeroAlloc.Mediator.Authorization.Tests` — runtime + DI integration

| Scenario | Test |
|---|---|
| Throw path: allowed → handler returns | passing policy returns T, handler invoked once |
| Throw path: denied → exception | failing policy throws `AuthorizationDeniedException` |
| Result path: allowed → `Success(T)` | `IAuthorizedRequest<T>` + passing → `Result.Success(T)` |
| Result path: denied → `Failure(F)` | failing → `Result.Failure(F)`, handler NOT invoked |
| Multi-policy AND short-circuit | second policy NEVER invoked when first fails (spy policy throws if called) |
| Stacking order preserved | `[Authorize]` declaration order = evaluation order |
| Cancellation mid-policy | pre-cancelled `CancellationToken` causes `OperationCanceledException`, not deny |
| Pipeline ordering | logging at `Order=-2000` runs BEFORE authz; one at `Order=0` runs AFTER |
| Eager validation: missing context source | `WithAuthorization()` without `Use*` throws at app build |
| Eager validation: unregistered policy | policy in `[Authorize]` not in DI throws at app build |
| Auto-registration | `WithAuthorization()` registers all discovered policies; verify via `sp.GetService<TPolicy>()` |
| Auto-registration opt-out | `opts.AutoRegisterDiscoveredPolicies = false` skips, user must register manually |

### `tests/ZeroAlloc.Mediator.Authorization.Generator.Tests` — generator snapshots

xUnit + [Verify](https://github.com/VerifyTests/Verify) + Roslyn `CSharpCompilation`.

| Snapshot | Input |
|---|---|
| `single-policy-throw-path.verified.cs` | one `IRequest<T>` + one `[Authorize]` + one `[AuthorizationPolicy]` |
| `single-policy-result-path.verified.cs` | one `IAuthorizedRequest<T>` + one `[Authorize]` + one `[AuthorizationPolicy]` |
| `stacked-policies.verified.cs` | two `[Authorize]` on one request |
| `multi-request-multi-policy.verified.cs` | two requests, three policies, varying combinations |
| `no-authz-in-compilation.verified.cs` | no `[Authorize]` anywhere — generator emits nothing |

Diagnostic tests (no Verify, just assertions): one per `ZAMA001..ZAMA006`.

### AOT smoke + allocation gate

The repo already has `samples/ZeroAlloc.Mediator.AotSmoke/Program.cs`. Extend it with one authorized-request scenario plus four `AllocationGate.AssertBudget(0, 1000, ...)` calls over the four allow-paths (throw + result × sync + async). Re-uses the `AllocationGate` helper pattern from `ZeroAlloc.Authorization` (~70 LOC copy).

## Compatibility & evolution

When `ZeroAlloc.Authorization` ships new contract features, they fall into three buckets:

### Bucket 1: Transparent (no host work needed)

- New `IAuthorizationPolicy` methods with default-interface implementations.
- New `ISecurityContext` properties with default values via DIM.
- New contract types the host doesn't reference.
- CI / benchmark / AOT gate work in the contract repo (e.g., Authorization #6, already shipped).

### Bucket 2: Generator update required

- **Authorization #1 — Policy composition (`Mode = AuthorizeMode.Any`).** Generator must read `Mode` and emit OR-evaluation. Without the update, generator silently emits AND code → semantic regression. Mitigation: ZAMA005.
- **Authorization #2 — Parameterized policies (`[Authorize("MinAge", 18)]`).** Generator must forward constructor args. Without the update, args silently ignored.

### Bucket 3: Runtime + DI surface change required

- **Authorization #3 — Resource-based authz (`IResourceSecurityContext<TRequest>`).** Host must populate the typed-resource context with the request being dispatched.
- **Authorization #4 — Rich failure shape evolution.** If `AuthorizationFailure` evolves with breaking changes, host's `AuthorizationDeniedException` and `IAuthorizedRequest<T>` change too. Major-version bump.
- **Authorization #5 — Source-generated policy registry shipped in the contract.** This sub-package's `GeneratedAuthorizationLookup` generator gets deleted; the runtime behavior migrates to consume the contract's `AuthorizerFor<TRequest>` via DI generic dispatch (matching `Mediator.Validation`'s exact pattern).

### Version compatibility matrix (lives in README)

| `ZeroAlloc.Mediator.Authorization` | Requires `ZeroAlloc.Authorization` | Mediator family version | Notes |
|---|---|---|---|
| 4.1.x | ≥ 1.1.0 | 4.x family | Baseline. Sub-package versioned in lockstep with the rest of the Mediator family. |
| 4.2.x | ≥ 1.2.0 (with #1) | 4.x family | Adds `Mode` support |
| 4.3.x | ≥ 1.3.0 (with #2) | 4.x family | Adds parameterized policies |
| 5.0.x | ≥ 2.0.0 | 5.x family | Major bump if either dep lands a major |

### Cross-repo bookkeeping

One follow-up update lands in another repo:

- **`ZeroAlloc.Authorization` `docs/backlog.md`** gets a new "Host coupling notes" subsection per backlog item flagging which require host changes vs which are transparent. Keeps the contract maintainer aware.

## Out of scope (explicit non-goals for v1)

1. **Multi-policy OR semantics** (`Mode = AuthorizeMode.Any`). Authorization backlog #1.
2. **Parameterized policies** (`[Authorize("MinAge", 18)]`). Authorization backlog #2.
3. **Resource-based authorization** (`IResourceSecurityContext<TRequest>`). Authorization backlog #3.
4. **Rich failure shape evolution** beyond what the contract ships today. Authorization backlog #4.
5. **`INotification` authorization.** Notifications fan-out to multiple handlers; deny semantics ambiguous; no return value to wrap. ZAMA006 if `[Authorize]` appears on a notification.
6. **ASP.NET Core helper sub-package.** No `WithHttpContext()` opt-in. Users wire `UseSecurityContextFactory(sp => /* read HttpContext */)` themselves.
7. **Streaming requests (`IStreamRequest<T>`).** Tricky deny semantics; defer.
8. **Per-handler `[Authorize]` (vs per-request).** Only request-type-level in v1.
9. **Conditional / runtime policy resolution.** Policy names are compile-time string literals only.
10. **CPU-overhead perf-regression CI gate.** Allocation gate covers heap; CPU is benchmarks territory.

## Versioning

Sub-package is versioned in lockstep with the rest of the Mediator family — currently 4.0.0, so first release of `ZeroAlloc.Mediator.Authorization` will be the next minor bump (likely `4.1.0`) of the entire family.
