# Mediator.Authorization v2.0.0 — Design

**Date:** 2026-05-19
**Status:** Brainstormed and approved; ready for implementation plan
**Tracks:** Coupled follow-on to `ZeroAlloc.Authorization` v2.0.0 (shipped in PR #19, [design doc](../../../ZeroAlloc.Authorization/docs/plans/2026-05-19-policy-registry-generator-design.md))
**Versioning impact:** `ZeroAlloc.Mediator.Authorization` lockstep-with-core v4.1.4 → **independent v2.0.0** (split versioning starts here). `ZeroAlloc.Mediator` core stays at v4.x — no bump for this release.

## Context

`ZeroAlloc.Authorization` v2 lifted the source generator from the Mediator.Authorization extension into the contract repo. The contract package now emits `AuthorizerFor<TRequest>` dispatchers and an `AddZeroAllocAuthorization()` DI extension. The Mediator.Authorization extension still ships the old plumbing it no longer needs: an in-package generator, ~50 LOC of static delegate state in `MediatorAuthorizationGeneratedHooks`, plus dead options on `AuthorizationOptions`.

This release deletes that plumbing and rewrites `AuthorizationBehavior` to consume `AuthorizerFor<TRequest>` via DI generic dispatch — matching the proven `Mediator.Validation` pattern. The extension shrinks from ~150 LOC to ~30 LOC.

## Goal

Ship `ZeroAlloc.Mediator.Authorization` v2.0.0 as a thin pipeline behavior over `ZeroAlloc.Authorization` v2's contract-side registry. Preserve the user-facing API surface where reasonable; delete only internal plumbing.

Net effect: ~150 LOC deleted, one canonical "decorate, generate, DI-resolve" pattern shared with the rest of the ZA family, and per-package versioning that lets future Authorization-only changes ship without dragging Mediator core along.

## Design decisions

Three decisions locked during brainstorming.

### D1 — Versioning strategy: split from core, independent v2.0.0

Authorization extension versions independently starting at v2.0.0. Mediator core stays at v4.x. Future Authorization breaks no longer force a core major bump.

Rejected:
- **Lockstep break** — bump both to v5.0.0. Wasteful for non-Auth users; every future Authorization break would force a core bump.
- **Defer until core has its own breaking change** — leaves real value on the table. The graduation signal from ZA.Authorization v2 is already cashed; sitting on the downstream consumer weakens the marketing story.

### D2 — Preserve user-facing API; delete only internal plumbing

`WithAuthorization()` builder and `AuthorizationOptions` keep their security-context-source methods (`UseSecurityContextFactory` / `UseAnonymousSecurityContext` / `UseAccessor<T>`). The dual `IRequest<T>`-throw and `IAuthorizedRequest<T>`-Result paths survive bit-for-bit. Minimal user-side migration: add one new line (`services.AddZeroAllocAuthorization();`) before `AddMediator()`.

Dropped (internal-only):
- `AutoRegisterDiscoveredPolicies` flag — contract-side `AddZeroAllocAuthorization()` handles registration unconditionally
- `ValidatePoliciesAreRegistered` eager startup check — DI throws clearly at first use
- `MediatorAuthorizationGeneratedHooks` static delegate plumbing — replaced by DI generic dispatch
- Entire `ZeroAlloc.Mediator.Authorization.Generator` project — contract-side generator does the codegen now

Rejected:
- **Aggressive simplification** — drop `AuthorizationOptions`, force users to register `ISecurityContext` themselves via raw DI. Bigger migration burden; inconsistent with ZA.Authorization v2's "kill internals, preserve user-facing API" stance.
- **Preserve everything including dead internals** — keep `AutoRegisterDiscoveredPolicies` and `ValidatePoliciesAreRegistered` as escape hatches. Fights the consolidation pitch; preserves the wrong abstraction.

### D3 — Two explicit DI calls, with a startup-time guard for the forgot-one case

User wires up:

```csharp
services.AddZeroAllocAuthorization();                          // contract-side: registry
services.AddMediator(b => b.WithAuthorization(auth =>          // Mediator-side: behavior + security-context source
    auth.UseAccessor<MySecurityContextAccessor>()));
```

Matches MS convention (`AddAuthorization()` + `AddAuthorizationCore()` are separate). Explicit, AOT-pure, source-gen-friendly. The layering is real: the contract registration can be consumed without Mediator (for a future MVC integration), so forcing it explicit serves multiple consumers cleanly.

`WithAuthorization()` adds a startup-time guard that catches the forgot-to-call-AddZeroAllocAuthorization case:

```csharp
var hasGeneratedRegistry = false;
foreach (var sd in builder.Services)
{
    if (sd.ServiceType.IsGenericType
        && sd.ServiceType.GetGenericTypeDefinition() == typeof(AuthorizerFor<>))
    {
        hasGeneratedRegistry = true;
        break;
    }
}
if (!hasGeneratedRegistry)
{
    throw new InvalidOperationException(
        "WithAuthorization() requires services.AddZeroAllocAuthorization() to be called first. " +
        "Add 'services.AddZeroAllocAuthorization();' before 'services.AddMediator(...)' in Program.cs. " +
        "This call is generated by the ZeroAlloc.Authorization source generator and registers your " +
        "[Policy]-decorated classes plus the AuthorizerFor<TRequest> dispatchers as scoped services. " +
        "If you have no [Policy]/[RequirePolicy] usage yet, remove WithAuthorization() from your builder.");
}
```

AOT-safe (enumerates registered `ServiceDescriptor`s, no reflection on requests). Imposes a call-order requirement (`AddZeroAllocAuthorization()` before `AddMediator()`) — the only sensible order, named explicitly in the error message.

Rejected:
- **Convention-based reflection at builder time** — `WithAuthorization()` reflectively looks up `GeneratedAuthorizationRegistration` in loaded assemblies and invokes its extension. Brittle (assembly load timing), needs trim annotations, reflection at startup.
- **Static-delegate handshake via `[ModuleInitializer]` on the contract side** — reintroduces the static state pattern we just deleted, plus a module-init-ordering footgun (if `Program.cs` reaches `WithAuthorization()` before the consumer assembly is loaded, the delegate is null and silent skip).

## Architecture

`ZeroAlloc.Mediator.Authorization` v2 package (single NuGet, no bundled generator):

```
ZeroAlloc.Mediator.Authorization/
├── src/ZeroAlloc.Mediator.Authorization/
│   ├── AuthorizationBehavior.cs                            (rewritten — ~25 LOC)
│   ├── MediatorAuthorizationServiceCollectionExtensions.cs (rewritten — WithAuthorization() + new guard)
│   ├── AuthorizationOptions.cs                             (slimmed — 3 security-context methods, no AutoRegister/Validate)
│   ├── IAuthorizedRequest.cs                               (unchanged — Result-path marker)
│   └── AuthorizationDeniedException.cs                     (unchanged — throw-path exception)
└── DELETED:
    ├── src/ZeroAlloc.Mediator.Authorization.Generator/     (entire project — 6 source files)
    ├── MediatorAuthorizationGeneratedHooks.cs              (~50 LOC static delegate state)
    └── tests/.../MediatorAuthorizationGeneratedHooksTests.cs
```

Dependencies pulled in via `ZeroAlloc.Authorization >= 2.0.0`:
- Bundled source generator (discovers `[Policy]` + `[RequirePolicy]`, emits `AuthorizerFor<T>` + `AddZeroAllocAuthorization()`)
- `AuthorizerFor<TRequest>` abstract base
- `[Policy]`, `[RequirePolicy]` attributes
- `IAuthorizationPolicy` (single async method)
- `ISecurityContext`, `AuthorizationFailure`, `AnonymousSecurityContext`

## Data flow

**Build time:** the contract-side generator runs in the consumer's compilation. Emits `GeneratedAuthorizerFor_<Request>` per `[RequirePolicy]` target plus `AddZeroAllocAuthorization()` extension. Five compile-time diagnostics (ZAUTH001–005) cover misconfiguration.

**Startup:**

```csharp
services.AddZeroAllocAuthorization();                          // scoped policy + AuthorizerFor<T> registrations
services.AddMediator(b => b.WithAuthorization(auth =>          // behavior + security-context source
    auth.UseAccessor<MySecurityContextAccessor>()));
```

`WithAuthorization()` does three things at registration:
1. Validates the configure callback called exactly one of the three security-context-source methods (throw otherwise — unchanged from v1).
2. Runs the new guard checking for `AuthorizerFor<>` registrations (throw if missing — D3).
3. Registers `AuthorizationOptions` as a singleton and `AuthorizationBehavior<,>` as a pipeline behavior at order `-1000`.

**Per-request dispatch — `IRequest<T>` (throw path):**

```
mediator.Send(new DeleteUserCommand(...), ct)
  ↓
Mediator's generated switch dispatches to handler chain
  ↓
AuthorizationBehavior<DeleteUserCommand, Unit>.Handle
  1. resolve ISecurityContext via configured source
  2. sp.GetService<AuthorizerFor<DeleteUserCommand>>()  →  GeneratedAuthorizerFor_DeleteUserCommand or null
  3. authorizer is null  →  fail-open, call next()
  4. authorizer.EvaluateAsync(ctx, ct)  →  UnitResult.Success / Failure
  5. IsFailure  →  throw new AuthorizationDeniedException(result.Error)
  6. IsSuccess  →  await next()
```

**Per-request dispatch — `IAuthorizedRequest<T>` (Result path):**

Steps 1–4 identical. Steps 5/6 return a `Result<T, AuthorizationFailure>` value instead of throwing. The dispatch path is selected at JIT time by the request's marker interface — no runtime check overhead.

**Allocation profile (happy path, both shapes):** 0 bytes. DI lookups cache hits, `ValueTask` wraps sync results on the stack, no string keys at runtime, no reflection. `AllocationBudgetTests` continues to gate this.

## Error handling

| Condition | Behavior |
|---|---|
| User forgot `services.AddZeroAllocAuthorization()` (or called it after `AddMediator`) | `WithAuthorization()` throws `InvalidOperationException` at startup with the exact missing line and ordering hint (D3 guard). |
| User forgot to configure security-context source in `WithAuthorization()` | Throws `InvalidOperationException` at startup with actionable message — unchanged from v1. |
| Policy registered but its `IAuthorizationPolicy` constructor dependencies aren't | `GetRequiredService<TPolicy>` inside the generated `AuthorizerFor<T>` throws `InvalidOperationException` at first dispatch with the policy's CLR type name. v1 caught this in eager validation; v5 catches it on first use. |
| Policy evaluation returns failure on `IRequest<T>` | Behavior throws `AuthorizationDeniedException(failure.Code, failure.Reason)` — unchanged from v1. |
| Policy evaluation returns failure on `IAuthorizedRequest<T>` | Behavior returns `Result<T, AuthorizationFailure>.Failure(failure)` — no throw, unchanged from v1. |
| Request has no `[RequirePolicy]` (no `AuthorizerFor<T>` generated for it) | Behavior calls `next()` — fail-open, matches `Mediator.Validation` pattern. The compile-time ZAUTH001 (unknown policy name on `[RequirePolicy]`) is the safety net for the realistic typo class. |

## AOT story

- All policy resolution is statically-typed `GetRequiredService<TConcrete>()` baked into the contract-side generator's output. No reflection in the runtime hot path.
- `sp.GetService<AuthorizerFor<TRequest>>()` is closed-generic DI lookup against pre-registered closed-type implementations — no open-generic resolution at runtime.
- `AuthorizationBehavior<TRequest, TResponse>` is plain C# — no reflection, no boxing of `UnitResult<AuthorizationFailure>` (value type all the way).
- Mediator.Authorization v2 ships no `[DynamicallyAccessedMembers]` annotations — none needed.
- `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs` gets rewritten to use the new DI-based dispatch. Both throw-path and Result-path scenarios preserved; `AllocationGate` continues to enforce ≤ 0 bytes per `EvaluateAsync` on the happy path.

## Testing strategy

| File | Status | Coverage |
|---|---|---|
| `AuthorizationBehaviorTests.cs` | rewritten | Allow path, deny on `IRequest<T>` → throw, deny on `IAuthorizedRequest<T>` → Result, multi-policy AND semantics, fail-open on missing `AuthorizerFor<T>`, scoped lifetime, cancellation propagation |
| `WithAuthorizationTests.cs` | rewritten + new tests | Existing security-context-source validation; **NEW** missing-AddZeroAllocAuthorization guard (positive + negative + wrong-order); idempotency on double registration |
| `IAuthorizedRequestTests.cs` | rewritten | Marker-interface-based Result-returning path; success + failure round-trip |
| `AuthorizationDeniedExceptionTests.cs` | unchanged | Exception shape unchanged |
| `AllocationBudgetTests.cs` | rewritten | Allow happy path zero-alloc gate; `IAuthorizedRequest<T>` deny zero-alloc gate; throw-path documented (not gated) |
| `MediatorAuthorizationGeneratedHooksTests.cs` | DELETED | Tested code that no longer exists |

The test project pins `ZeroAlloc.Authorization >= 2.0.0`. Test policies use v2 attributes (`[Policy]` / `[RequirePolicy]`) and the simplified async `EvaluateAsync` contract.

**CI gates for v2 to ship:**
- All test files green (after the deletion + 5 new guard tests)
- AOT smoke binary builds with `PublishAot=true`, new scenario passes its zero-alloc gate
- `dotnet pack` produces `ZeroAlloc.Mediator.Authorization.2.0.0.nupkg` independent of core's `.4.x.nupkg`, pinning `ZeroAlloc.Authorization >= 2.0.0` as a hard floor
- `apicompat-suppressions.xml` updated for the v2 breaks
- `release-please` config split so the package versions independently from core

## Migration & versioning

**`apicompat-suppressions.xml` additions** (the v2 breaking-change documentation):

The repo's existing root `apicompat-suppressions.xml` gains entries for:
- `T:ZeroAlloc.Mediator.Authorization.MediatorAuthorizationGeneratedHooks` (CP0001 — type removed)
- `M:ZeroAlloc.Mediator.Authorization.AuthorizationOptions.AutoRegisterDiscoveredPolicies.get` (CP0002 — property removed)
- `M:ZeroAlloc.Mediator.Authorization.AuthorizationOptions.ValidatePoliciesAreRegistered` (CP0002 — method removed)
- Any internal types in the deleted Generator project that happened to be `public` (audit during implementation)

Each entry gets `IsBaselineSuppression=true` + explicit per-TFM `Left`/`Right` paths matching the pattern shipped in ZA.Authorization v2's PR #19.

**`release-please-config.json` split:**

The repo currently treats both packages as a single release component. The v2 prep splits it so Authorization can version from `2.0.0` independently. Commits scoped to `src/ZeroAlloc.Mediator.Authorization/**` (or with `(authorization)` scope) bump only the Authorization package; commits scoped to core only bump core.

**Conventional-commits scoping for release-please:**

- `feat(authorization)!: rewrite AuthorizationBehavior to consume AuthorizerFor<T> via DI`
- `feat(authorization)!: delete in-package generator + static MediatorAuthorizationGeneratedHooks`
- `feat(authorization)!: split versioning — independent v2.0.0 release line`
- `chore(authorization): drop ValidatePoliciesAreRegistered + AutoRegisterDiscoveredPolicies`
- `test(authorization): rewrite suite for DI-based dispatch; delete hook tests`
- `build(authorization): suppress intentional v2 breaking-API diagnostics`
- `docs(authorization): update samples + host integration for new two-call pattern`

**Release sequencing:**

1. `ZeroAlloc.Authorization` v2.0.0 already shipped (PR #19 merged); v2 docs sweep follows (PR #21).
2. `ZeroAlloc.Mediator.Authorization` v2.0.0 ships as the coupled follow-on. Pins `ZeroAlloc.Authorization >= 2.0.0`.
3. `ZeroAlloc.Mediator` core continues at v4.x — no bump for this release.

**Compatibility window:** zero. The user upgrades both `ZeroAlloc.Authorization` and `ZeroAlloc.Mediator.Authorization` in one go. Mixed versions (Mediator.Authorization v2 + ZA.Authorization v1) won't compile because v2's generated `AddZeroAllocAuthorization()` and `AuthorizerFor<T>` base don't exist on the v1 contract.

**Consumer migration example (Program.cs):**

```csharp
// Before (Mediator.Authorization v4.1.x):
services.AddMediator(b => b.WithAuthorization(auth =>
    auth.UseAccessor<MySecurityContextAccessor>()));   // generator's [ModuleInitializer] silently registered policies

// After (Mediator.Authorization v2.0.0 + ZeroAlloc.Authorization v2.0.0):
services.AddZeroAllocAuthorization();                  // NEW — generator-emitted, registers [Policy] classes + AuthorizerFor<T>
services.AddMediator(b => b.WithAuthorization(auth =>
    auth.UseAccessor<MySecurityContextAccessor>()));   // SAME — security-context source unchanged
```

Plus the per-policy migration (rename attributes, simplify `IAuthorizationPolicy` implementation) inherited from ZA.Authorization v2.

## Out of scope (deferred to v2.x)

- Splitting versioning for the rest of the Mediator extensions (Validation, Telemetry, etc.). If they ever need a breaking change of their own, the split pattern is already in place from this release.
- Any change to Mediator core's API. The core API stays at v4.x exactly as-is.
- `AuthorizationDeniedException` redesign. The shape (Code, Reason carried from `AuthorizationFailure`) is well-understood by existing users; no reason to break it.

Each is additive in v2.x without further breaking changes.
