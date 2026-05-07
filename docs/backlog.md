# ZeroAlloc.Mediator — Backlog

Candidate enhancements identified during real-world usage and post-merge code reviews. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

Items here are sub-package-scoped where applicable; the sub-package is named in parentheses.

---

## 1. Generator: migrate to `ForAttributeWithMetadataName` incremental pipeline (`Mediator.Authorization`)

**What:** the current `AuthorizationGenerator` uses `context.RegisterSourceOutput(context.CompilationProvider, ...)` which re-runs the full discovery + emission on every compilation change — every keystroke in the IDE walks the global namespace end-to-end. The point of `IIncrementalGenerator` is `ForAttributeWithMetadataName`-based pipelines so the generator only re-runs when relevant syntax changes.

**Why:** for a small project this is invisible; for a large solution with hundreds of types the cost compounds — measurable IDE lag.

**Work:** rewrite `Initialize` to use:
```csharp
var policies = context.SyntaxProvider.ForAttributeWithMetadataName(
    "ZeroAlloc.Authorization.AuthorizationPolicyAttribute",
    static (node, _) => node is ClassDeclarationSyntax,
    static (ctx, _) => ExtractPolicy(ctx))
    .Where(static p => p is not null)
    .Collect();

var requests = context.SyntaxProvider.ForAttributeWithMetadataName(
    "ZeroAlloc.Authorization.AuthorizeAttribute",
    static (node, _) => node is TypeDeclarationSyntax,
    static (ctx, _) => ExtractRequest(ctx))
    .Collect();

context.RegisterSourceOutput(policies.Combine(requests), Emit);
```

Also requires extracting models that are properly value-equatable (currently `PolicyModel`/`RequestModel` carry `Microsoft.CodeAnalysis.Location`, which uses reference equality — defeats the cache between pipeline runs). Replace with `LinePositionSpan` + file path; rebuild `Location` at diagnostic-report time.

**Risk:** medium — model serialization across pipeline stages is a known-tricky area. Test by enabling generator-cache verification and rebuilding incrementally.

**Graduation signal:** a user reports IDE lag in a real project, OR the org standardizes on this pattern across all generators (Mediator.Generator, Saga.Generator, etc.).

---

## 2. Generator: switch FQN-string symbol comparisons to `SymbolEqualityComparer` lookups (`Mediator.Authorization`)

**What:** `PolicyDiscovery` and `RequestDiscovery` compare types by FQN strings (e.g. `"ZeroAlloc.Mediator.IRequest<TResponse>"`). This works but is fragile — if the contract package ever renames a type parameter (e.g. `IRequest<TResp>`), the string match silently fails and the generator emits nothing. Same risk applies to the `INotification`, `IAuthorizationPolicy`, `[Authorize]`, `[AuthorizationPolicy]` lookups.

**Why:** symbol comparison via `compilation.GetTypeByMetadataName(...)` + `SymbolEqualityComparer.Default` is robust against rename, refactoring, and namespace changes.

**Work:** cache the contract symbols once per `Discover` invocation; replace string comparisons with `SymbolEqualityComparer.Default.Equals(symbol, contractSymbol)`.

**Risk:** low — mechanical refactor with existing snapshot tests verifying behavior.

**Graduation signal:** any contract type-parameter rename, OR opportunistically alongside item #1.

---

## 3. Generator: `[Authorize]` discovery should reject non-public/non-internal policy classes (`Mediator.Authorization`)

**What:** today `PolicyDiscovery` walks nested types but doesn't check `INamedTypeSymbol.DeclaredAccessibility`. A `private` policy class nested inside a public class would be discovered, but `services.AddScoped<global::Outer.FooPolicy>()` won't compile from the generator-emitted DI extensions (private accessibility violation).

**Why:** discovered policies must be referenceable from the generated code. Anything below `internal` in an assembly the user's code consumes is unreachable.

**Work:** filter `type.DeclaredAccessibility ∈ { Public, Internal }` in the discovery walk. Emit a new `ZAMA007` warning when a `[AuthorizationPolicy]`-decorated type is below internal — guides the user to `internal sealed` at minimum.

**Risk:** low — additive filter + new diagnostic.

**Graduation signal:** a user reports a confusing build error from generated code referencing a private nested policy class.

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

**What:** the `samples/.../AuthorizedScenario.cs` allocation-gate calls measure `policy.IsAuthorized(ctx)` and `Evaluate(ctx)` — those are calls into the `ZeroAlloc.Authorization` library, NOT into Mediator.Authorization's wiring. The Tests-side `Behavior_*Allow_ZeroAllocation` tests do measure `Handle` correctly.

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

## 8. Generator: edge-case snapshot tests (`Mediator.Authorization`)

**What:** the v1 snapshot tests cover top-level types in the global namespace. Real-world generator emissions need to handle:

- Request types with generic responses (`IRequest<List<int>>`, `IRequest<Result<int, MyError>>`).
- Request types with nullable responses (`IRequest<int?>`).
- Request types in nested namespaces (`MyApp.Domain.Orders.GetOrderById`).
- Request types that are nested classes (`Outer.GetOrderById`).
- Policy classes in nested namespaces.
- Policy names containing characters that need C# string escaping (quotes, backslashes — `LookupEmitter.EscapeStringLiteral` exists but is never exercised by a test).

**Why:** string-based emission is exactly where these edge cases blow up. Each missing case is a latent build error in someone's compilation.

**Work:** add ~6 snapshot tests, one per case above.

**Risk:** low — pure tests; the emitter may need small tweaks if a case isn't already handled.

**Graduation signal:** any user-reported "compile error from generated code in my project that doesn't reproduce in the test project."

---

## 9. Test: negative-diagnostic test (`Mediator.Authorization`)

**What:** today's `DiagnosticTests` assert each of `ZAMA001`–`ZAMA006` *fires* on triggering source. None assert that a clean source produces *zero* diagnostics.

**Why:** a regression where ZAMA001 starts firing on legitimate `[Authorize("RealPolicy")]` declarations would not be caught. The 5 snapshot tests don't fail on diagnostic-noise (they only check emitted code).

**Work:** add a single `Clean_Source_Emits_No_Diagnostics` test that compiles a known-good source (e.g. one of the snapshot test fixtures) and asserts `diagnostics.Length == 0`.

**Risk:** trivial — single test.

**Graduation signal:** ship alongside item #4 / #5.

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
