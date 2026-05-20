# ZeroAlloc.Mediator — Backlog

Candidate enhancements identified during real-world usage and post-merge code reviews. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

Items here are sub-package-scoped where applicable; the sub-package is named in parentheses.

---

## 1. ✅ Generator: migrate to `ForAttributeWithMetadataName` incremental pipeline (`Mediator.Authorization`) — **shipped (moved upstream)**

**Shipped:** 2026-05-19 via PR #87 (`Mediator.Authorization` v2.0.0). The source generator no longer lives in this repo — it consolidated into `ZeroAlloc.Authorization` v2, which uses the `ForAttributeWithMetadataName` pipeline against the new `[Policy]` / `[RequirePolicy]` contract attributes. This host now ships only the pipeline behavior + builder.

Original motivation: the v1 `AuthorizationGenerator` re-ran full discovery on every compilation change. The upstream rewrite both fixes that and removes duplication across hosts (Mediator.Authorization + AI.Sentinel).

---

## 2. ✅ Generator: switch FQN-string symbol comparisons to `SymbolEqualityComparer` lookups (`Mediator.Authorization`) — **shipped (moved upstream)**

**Shipped:** 2026-05-19 via PR #87. Same disposition as item #1 — the upstream generator in `ZeroAlloc.Authorization` v2 uses `compilation.GetTypeByMetadataName(...)` + `SymbolEqualityComparer.Default` throughout, so contract type-parameter renames are safe.

---

## 3. ✅ Generator: `[RequirePolicy]` discovery should reject non-public/non-internal policy classes (`Mediator.Authorization`) — **shipped (moved upstream)**

**Shipped:** 2026-05-19 via PR #87. Handled by the upstream generator in `ZeroAlloc.Authorization` v2; a `[Policy]`-decorated class below `internal` raises a diagnostic in the contract package's `ZAUTH`-prefixed diagnostic set.

---

## 4. Test: pipeline-ordering integration test (`Mediator.Authorization`)

**What:** the design specifies `[PipelineBehavior(Order = -1000)]` so authorization runs before validation/cache/logging. The constant is asserted in source code, but no test proves the order actually takes effect end-to-end.

**Why:** an attribute constant is documentation, not enforcement. A future refactor of Mediator's pipeline-ordering algorithm could break the assumed ordering invisibly.

**Work:** add a test that registers both `WithAuthorization()` and `WithValidation()`, sends a request whose validator would throw if reached, and asserts the auth-deny path short-circuits before validation. Drive the full pipeline via `IMediator.Send(...)` rather than calling `Handle` directly.

**Risk:** low — single new test in `tests/ZeroAlloc.Mediator.Authorization.Tests/`.

**Graduation signal:** trivial — should ship before v1 ships externally to consumers. Or: any incident where ordering surprised the user.

---

## 5. Test: end-to-end behavior test through `IMediator.Send` (`Mediator.Authorization`)

**What:** today's `AuthorizationBehaviorTests` invoke the static `Handle<TRequest, TResponse>` directly with a mocked `next`. The smoke binary does the same. **No test in the v1 PR exercises the generator-emitted `[ModuleInitializer]` wiring through the real `IMediator` dispatcher.**

**Why:** the unit-level tests cover the behavior's logic, but they don't prove the full chain (hooks Configure → behavior receives lookups → handler invoked) works under DI/dispatcher routing. Combined with item #4, this is the gap between "the unit works" and "the integration works."

**Work:** add at least one test using `services.BuildServiceProvider().GetRequiredService<IMediator>().Send(...)` so the wiring is exercised end-to-end. Allow path + deny path.

**Risk:** low — single test pair.

**Graduation signal:** trivial — same as item #4.

---

## 6. Sample: AOT smoke binary should measure `Handle` allocation, not just policy library (`Mediator.Authorization`)

**What:** the `samples/.../AuthorizedScenario.cs` allocation-gate calls measure `policy.EvaluateAsync(ctx)` directly — that's a call into the `ZeroAlloc.Authorization` library, NOT into Mediator.Authorization's wiring. The Tests-side `Behavior_*Allow_ZeroAllocation` tests do measure `Handle` correctly.

**Why:** the AOT-side gate's job is to certify Mediator.Authorization's runtime under the AOT runtime. Today's gate certifies the underlying policy library (already certified in `ZeroAlloc.Authorization`). The handler's allocation profile under AOT is unverified.

**Work:** restructure the smoke binary's gate calls to invoke `AuthorizationBehavior.Handle<TRequest, TResponse>(...)` directly (or via the dispatcher) instead of the policy method. Need to set up a real `ServiceProvider` + `ISecurityContext` inside the smoke; the existing `InternalsVisibleTo` to the smoke binary already gives access to `AuthorizationBehaviorState`.

**Risk:** low — refactor of one file; no behavior changes.

**Graduation signal:** ship alongside item #5 (both are about exercising the real wiring rather than mocked-out paths).

---

## 7. Cleanup: remove `InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"` once item #6 ships (`Mediator.Authorization`)

**What:** `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj` declares `<InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />`. The smoke binary uses it to poke `AuthorizationBehaviorState.ServiceProvider` directly, bypassing the full DI roundtrip.

**Why:** `InternalsVisibleTo` to a sample is a leak — production code shouldn't be aware of a sample's internals. Once item #6 routes through DI properly, this entry can go.

**Work:** trivially remove the line + update PublicAPI.Unshipped.txt if needed.

**Risk:** low — depends on item #6 first.

**Graduation signal:** item #6 lands.

---

## 8. ✅ Generator: edge-case snapshot tests (`Mediator.Authorization`) — **shipped (moved upstream)**

**Shipped:** 2026-05-19 via PR #87. The snapshot-test suite for the generator now lives in `ZeroAlloc.Authorization` v2 alongside the generator itself. Covers nested namespaces, nested types, generic/nullable responses, and policy-name escaping.

---

## 9. ✅ Test: negative-diagnostic (no-noise) test (`Mediator.Authorization`) — **shipped (moved upstream)**

**Shipped:** 2026-05-19 via PR #87. The upstream `ZeroAlloc.Authorization` v2 test suite asserts that clean sources produce zero `ZAUTH001`–`ZAUTH005` diagnostics. The five compile-time diagnostics also moved upstream with the generator.

---

## 10. Org-wide: lift the `AllocationGate` helper into a shared internal-source package

**What:** the same ~70-LOC `AllocationGate.cs` helper has been copy-pasted into `ZeroAlloc.Authorization` (PR #11, Authorization backlog #6) and `ZeroAlloc.Mediator.Authorization` (Mediator #74). Two more packages (Cache, Resilience, etc.) are likely candidates as they certify their own zero-alloc claims.

**Why:** copy-paste works for v1 but drift is inevitable. A shared internal-only NuGet (or a shared source link via `<Compile Include="$(MSBuildThisDirectory)../shared/AllocationGate.cs" />`) keeps the helper consistent.

**Work:** likely a new repo `ZeroAlloc.TestHelpers` or a `tests/` shared subdirectory in `.github`. Each consuming package's tests + AOT smoke link the file. **Pre-graduation:** wait until 3+ packages have copied the helper independently — that's the friction signal that justifies factoring out.

**Risk:** medium — shared internals across the org are an ownership question, not just a technical one. Don't ship until at least one user-facing pain point makes it concrete (e.g. divergent helpers cause confusion, or a fix needs to be replicated 5 places).

**Graduation signal:** 3 packages have copied the helper AND a meaningful drift / fix has happened in at least one copy.

---

## Out of scope (for now)

- **Streaming-request authorization.** Mediator.Authorization v1 explicitly does NOT support `IStreamRequest<T>` — the deny semantics are tricky (deny before first item or mid-stream?). Defer until a real consumer surfaces.
- **Per-handler `[Authorize]` (vs per-request).** v1 only supports request-type-level. Putting `[Authorize]` on the handler class instead of the request type is rejected — the policy decision should be visible at the call site (request type), not buried in the handler implementation.
- **Conditional / runtime policy resolution.** Policy names are compile-time string literals only.
- **OpenTelemetry on the authz behavior.** The deny path is a domain-level signal; users can compose with `Mediator.Telemetry`. No special-casing in `Mediator.Authorization`.
