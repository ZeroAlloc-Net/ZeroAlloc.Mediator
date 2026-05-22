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

## 4. ✅ Test: pipeline-ordering integration test (`Mediator.Authorization`) — **shipped**

**Shipped:** 2026-05-22 via PR #93. `IntegrationTests.Pipeline_ordering_authorization_runs_before_later_behaviors` proves `AuthorizationBehavior`'s `Order=-1000` short-circuits before any later behavior fires. Swap-test (documented inline in the test file) confirms the assertion catches an inverted-Order regression.

Implementation needed a local pipeline-behavior shim (`AuthorizationBehaviorShim`) in the test assembly because Mediator's source generator only sees `[PipelineBehavior]`-decorated types in the current compilation; the real cross-assembly `AuthorizationBehavior` is invisible to it. The shim forwards to the real `Handle` and lets `IMediator.Send` exercise the full chain.

---

## 5. ✅ Test: end-to-end behavior test through `IMediator.Send` (`Mediator.Authorization`) — **shipped**

**Shipped:** 2026-05-22 via PR #93. `IntegrationTests.End_to_end_through_IMediator_Send_allow_path` and `..._deny_path` exercise the DI container build → `AuthorizationBehaviorAccessor` static-init → `IMediator.Send` → shim → real `AuthorizationBehavior.Handle` → `AuthorizerFor` → policy → handler chain end-to-end.

The shim trick from item #4 is what makes this possible; the same fixture supports both #4 and #5 (declared in `TestFixtures.cs`).

---

## 6. ✅ Sample: AOT smoke binary should measure `Handle` allocation, not just policy library (`Mediator.Authorization`) — **shipped**

**Shipped:** 2026-05-22 via PR #93. The `AuthorizationBehavior.Handle` allocation gate (512 B / 1000-iter envelope) had landed earlier; PR #93 completed the work by deleting the redundant `policy.EvaluateAsync` gate that was certifying `ZeroAlloc.Authorization` (already certified upstream) rather than `Mediator.Authorization`. The smoke's AllocationGate output is now scoped to this package's runtime.

---

## 7. ✅ Cleanup: remove `InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"` (`Mediator.Authorization`) — **shipped**

**Shipped:** 2026-05-22 via PR #93. Prerequisite: `AuthorizationBehaviorAccessor` + `AuthorizationBehaviorState` promoted from `internal` to `public` (additive PublicAPI change, also part of PR #93). The smoke now resolves `AuthorizationBehaviorAccessor` from DI to trigger its constructor side-effect, instead of writing `AuthorizationBehaviorState.ServiceProvider` via internals. The `InternalsVisibleTo` to `ZeroAlloc.Mediator.Authorization.Tests` was also dropped as a followup once the now-public types covered the test fixture's needs.

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
