# Mediator.Authorization Integration Tests Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land backlog items #4 (pipeline-ordering integration test), #5 (end-to-end via `IMediator.Send`), #6 (AOT smoke certifies Mediator.Authorization wiring, not policy library), and #7 (drop `InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"`).

**Architecture:** Three additions to the test fixture in `TestFixtures.cs` (a local pipeline-behavior shim that lets the test assembly's Mediator generator wire `AuthorizationBehavior` into `IMediator.Send`; a counting downstream behavior to prove ordering; one new policy/request/handler triple). One new test file `IntegrationTests.cs` with three test methods. One additive PublicAPI change promoting `AuthorizationBehaviorAccessor` and `AuthorizationBehaviorState` to `public` so the AOT smoke can resolve the accessor instead of touching the static directly. Refactor of the AOT smoke to use the accessor + drop the redundant policy-only allocation gate. Removal of one `<InternalsVisibleTo>` entry.

**Tech Stack:** .NET 10 / .NET 8 multi-targeted, xUnit 2.x, ZeroAlloc.Mediator's source-generator-driven pipeline (`[PipelineBehavior(Order = N)]` on `IPipelineBehavior`-implementing classes with `static Handle<TRequest, TResponse>`), ZeroAlloc.Authorization v2 source generator (`[Policy]` / `[RequirePolicy]`), Microsoft.CodeAnalysis.PublicApiAnalyzers (PublicAPI.Shipped.txt / Unshipped.txt convention).

**Design doc:** `docs/plans/2026-05-22-mediator-authorization-integration-tests-design.md`

**Working branch:** `test/mediator-authorization-integration-tests` (already created off `main` at `cf7ecd4`).

**Key context to keep in mind while implementing:**

- ZA.Mediator pipeline behaviors are sealed classes implementing the non-generic marker `IPipelineBehavior`, decorated with `[PipelineBehavior(Order = N)]`, exposing a `public static ValueTask<TResponse> Handle<TRequest, TResponse>(TRequest request, CancellationToken ct, Func<TRequest, CancellationToken, ValueTask<TResponse>> next)` method. **Not** a generic interface.
- The Mediator generator only sees behaviors in the **current compilation**. `AuthorizationBehavior` lives in the `ZeroAlloc.Mediator.Authorization` assembly and is **invisible** to the test assembly's generator. The shim added in Task 2 is what makes `IMediator.Send` exercise the real behavior — the generator picks up the shim, the shim forwards to `AuthorizationBehavior.Handle`.
- `AuthorizationBehaviorState.ServiceProvider` is a `static volatile` field. `AuthorizationBehaviorAccessor`'s constructor sets it as a side-effect. The ctor runs when something resolves `AuthorizationBehaviorAccessor` from DI. `WithAuthorization()` registers the accessor as a singleton with a factory — lazy until first resolution.
- Tests run under `[Collection("non-parallel-authorization")]` because the static service-provider field would stomp across parallel tests. New integration tests need the same collection.

---

## Task 1: Promote `AuthorizationBehaviorAccessor` + `AuthorizationBehaviorState` to public

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/src/ZeroAlloc.Mediator.Authorization/AuthorizationBehaviorAccessor.cs`
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt`

**Step 1: Promote both types**

Open `AuthorizationBehaviorAccessor.cs`. Replace the existing contents with:

```csharp
using System;

namespace ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // file groups behavior-public accessor + state
/// <summary>
/// Static carrier for the active <see cref="IServiceProvider"/> consumed by
/// <see cref="AuthorizationBehavior.Handle{TRequest, TResponse}"/>. Set via
/// <see cref="AuthorizationBehaviorAccessor"/>'s constructor on first DI
/// resolution after <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>.
/// </summary>
/// <remarks>
/// Public so non-Mediator hosts (samples, AOT smoke binaries) can resolve the
/// accessor explicitly instead of writing the field directly — see
/// <see cref="AuthorizationBehaviorAccessor"/>.
/// </remarks>
public static class AuthorizationBehaviorState
{
    /// <summary>The active provider for the authorization behavior, or <see langword="null"/> until first accessor construction.</summary>
    public static volatile IServiceProvider? ServiceProvider;
}

/// <summary>
/// DI-resolved hook whose constructor side-effects
/// <see cref="AuthorizationBehaviorState.ServiceProvider"/>. Registered as a
/// singleton by <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>;
/// resolve it once after <c>BuildServiceProvider()</c> to initialise the
/// behavior's view of the container.
/// </summary>
public sealed class AuthorizationBehaviorAccessor
{
    /// <summary>Stores the provided <paramref name="serviceProvider"/> into <see cref="AuthorizationBehaviorState.ServiceProvider"/>.</summary>
    public AuthorizationBehaviorAccessor(IServiceProvider serviceProvider) =>
        AuthorizationBehaviorState.ServiceProvider = serviceProvider;
}
#pragma warning restore MA0048
```

The only access-level changes are `internal` → `public` on the type declarations and the field/constructor. The runtime behaviour is identical.

**Step 2: Update PublicAPI.Unshipped.txt**

Open `PublicAPI.Unshipped.txt`. Add the following entries (alphabetical order matters for the PublicApiAnalyzer — insert in the right place):

```
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorAccessor
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorAccessor.AuthorizationBehaviorAccessor(System.IServiceProvider! serviceProvider) -> void
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorState
static ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorState.ServiceProvider -> System.IServiceProvider?
```

The `static ... .ServiceProvider -> System.IServiceProvider?` line covers a public `volatile` field. The PublicApiAnalyzer accepts the field-declaration syntax verbatim (no separate getter/setter entries for fields).

If your editor or analyzer suggests slightly different lines (e.g. nullable-annotation symbol placement), accept its suggestion — Roslyn's analyzer is the source of truth for the exact text. The `RS0016` / `RS0017` rules will fail the build if the lines are wrong.

**Step 3: Verify the build still passes**

Run from the repo root:

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet build src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj -c Release
```

Expected: build SUCCEEDED with 0 warnings, 0 errors. If `RS0016` or `RS0017` fires, fix the `PublicAPI.Unshipped.txt` lines per the analyzer's diagnostic message.

**Step 4: Commit**

```bash
git add src/ZeroAlloc.Mediator.Authorization/AuthorizationBehaviorAccessor.cs \
        src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt
git commit -m "feat(authorization): promote AuthorizationBehaviorAccessor + State to public

The smoke binary needs to resolve AuthorizationBehaviorAccessor from DI to
trigger its static-init side effect, instead of writing
AuthorizationBehaviorState.ServiceProvider directly via InternalsVisibleTo.
Additive PublicAPI change: no signature changes, no removals.

Prepares for InternalsVisibleTo \"ZeroAlloc.Mediator.AotSmoke\" removal
(backlog #7)."
```

---

## Task 2: Add integration-test fixture types

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/tests/ZeroAlloc.Mediator.Authorization.Tests/TestFixtures.cs`

**Step 1: Add the new fixture types**

Open `TestFixtures.cs`. Add these entries; placement guidance below.

Add a new policy alongside the existing policy declarations (e.g. after `CancellablePolicy`):

```csharp
[Policy("IntegrationTest")]
public sealed class IntegrationTestPolicy : IAuthorizationPolicy
{
    // Allow when the security context carries the "Admin" role; deny otherwise.
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "needs Admin"));
}
```

Add a new request type next to the existing request records (after `GetThingUnauthorized`):

```csharp
// Integration tests drive this through IMediator.Send. The shim below
// (AuthorizationBehaviorShim) is what the test-assembly's Mediator generator
// picks up to wire AuthorizationBehavior.Handle into the dispatcher; this
// request just exists to be sent.
[RequirePolicy("IntegrationTest")]
public sealed record IntegrationTestRequest(int Value) : IRequest<int>;
```

Add a handler in the stub-handler block (after `StubGetThingUnauthorizedHandler`):

```csharp
public sealed class IntegrationTestHandler : IRequestHandler<IntegrationTestRequest, int>
{
    public ValueTask<int> Handle(IntegrationTestRequest r, CancellationToken ct)
        => ValueTask.FromResult(r.Value * 2);
}
```

Add the counter holder, the shim, and the counting behavior at the bottom of the file (before the `[CollectionDefinition]` block):

```csharp
// Mutable counter resolved by CountingDownstreamBehavior to record whether
// downstream pipeline behaviors ran on a given Send. AddSingleton in tests.
internal sealed class InvocationCounter { public int Count; }

// Local shim — the test-assembly's Mediator generator sees this in the
// current compilation (cross-assembly AuthorizationBehavior is invisible to
// it). The shim's static Handle just forwards to the real behavior. Same
// Order constant (-1000), identical contract.
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehaviorShim : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => AuthorizationBehavior.Handle<TRequest, TResponse>(request, ct, next);
}

// Numerically AFTER the shim (-500 > -1000): runs only if the shim did NOT
// short-circuit. The integration tests assert this counter to prove
// pipeline ordering took effect.
[PipelineBehavior(Order = -500)]
public sealed class CountingDownstreamBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        // Reads via AuthorizationBehaviorState (now public after Task 1).
        // Tests can also read AuthorizationBehaviorState themselves — fine
        // since the field is public now.
        AuthorizationBehaviorState.ServiceProvider!
            .GetRequiredService<InvocationCounter>().Count++;
        return next(request, ct);
    }
}
```

Add an `using Microsoft.Extensions.DependencyInjection;` at the top of `TestFixtures.cs` if it's not already imported (the file already uses DI in test types, so verify).

**Step 2: Verify the test assembly still compiles**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet build tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj -c Release
```

Expected: SUCCEEDED, 0 errors. There may be `ZAM001` ("no handler registered") warnings if the source generator runs before DI registration — those are emitted during the build and should remain absent because `IntegrationTestHandler` is now declared in the same compilation. If they fire, double-check the handler class is `public sealed class IntegrationTestHandler : IRequestHandler<...>`.

**Step 3: Run the existing test suite — must remain green**

```bash
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj -c Release
```

Expected: every existing test passes. The new pipeline-behavior classes are wired into the test-assembly's `MediatorService` partial, but none of the existing tests use `IMediator.Send` — they all call `AuthorizationBehavior.Handle` directly — so the shim and counting behavior never execute in those paths.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Mediator.Authorization.Tests/TestFixtures.cs
git commit -m "test(fixtures): add integration-test shim + counting behavior

Adds AuthorizationBehaviorShim — a local [PipelineBehavior(Order=-1000)]
class whose static Handle forwards to AuthorizationBehavior.Handle. This
is the bridge that makes the test-assembly's Mediator generator wire the
real behavior into IMediator.Send (the cross-assembly behavior is
invisible to the generator).

Adds CountingDownstreamBehavior at Order=-500 (numerically after the
shim) for ordering assertions, plus an InvocationCounter holder, plus
IntegrationTestPolicy / IntegrationTestRequest / IntegrationTestHandler
used by the new integration tests in the next commit.

No existing tests use IMediator.Send, so the new pipeline behaviors do
not affect their behaviour — verified by the green test run."
```

---

## Task 3: Add `IntegrationTests.cs`

**Files:**
- Create: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/tests/ZeroAlloc.Mediator.Authorization.Tests/IntegrationTests.cs`

**Step 1: Write the test file**

Create `IntegrationTests.cs` with this content verbatim:

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using Xunit;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Integration tests covering backlog items #4 (pipeline ordering) and #5
// (end-to-end via IMediator.Send). These complement AuthorizationBehaviorTests,
// which drives AuthorizationBehavior.Handle directly with a mocked next
// delegate. The tests here exercise the full chain: DI container build →
// AuthorizationBehaviorAccessor sets the static → Mediator generator's
// dispatcher routes IMediator.Send through AuthorizationBehaviorShim →
// shim forwards to AuthorizationBehavior.Handle → AuthorizerFor + policy +
// CountingDownstreamBehavior + handler.
//
// All three tests use [Collection("non-parallel-authorization")] because
// AuthorizationBehaviorState.ServiceProvider is a static field; running in
// parallel would stomp it across tests.
//
// Swap-test for the ordering assertion's meaningfulness: temporarily change
// CountingDownstreamBehavior's attribute in TestFixtures.cs to
// [PipelineBehavior(Order = -2000)] (numerically BEFORE the shim's -1000).
// The Pipeline_ordering_authorization_runs_before_later_behaviors test must
// then FAIL with counter.Count == 1 — proving the test catches a real
// regression in pipeline ordering. Revert after verifying. Recommended any
// time the Mediator core's pipeline-ordering algorithm changes.
[Collection("non-parallel-authorization")]
public sealed class IntegrationTests
{
    [Fact]
    public async Task Pipeline_ordering_authorization_runs_before_later_behaviors()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With()); // no Admin → policy denies
        var mediator = sp.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await mediator.Send(new IntegrationTestRequest(42)));

        // Proves ordering: CountingDownstreamBehavior (Order=-500) never ran
        // because AuthorizationBehaviorShim (Order=-1000) short-circuited
        // ahead of it via AuthorizationDeniedException.
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task End_to_end_through_IMediator_Send_allow_path()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With("Admin"));
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new IntegrationTestRequest(7));

        Assert.Equal(14, result);          // handler invoked: 7 * 2
        Assert.Equal(1, counter.Count);    // downstream behavior ran on the allow path
    }

    [Fact]
    public async Task End_to_end_through_IMediator_Send_deny_path()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With()); // no Admin
        var mediator = sp.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await mediator.Send(new IntegrationTestRequest(7)));

        // Handler must NOT have run — the shim short-circuited via the throw.
        Assert.Equal(0, counter.Count);
    }

    private static ServiceProvider BuildProvider(InvocationCounter counter, ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddSingleton(counter);
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        var sp = services.BuildServiceProvider();

        // Trigger AuthorizationBehaviorAccessor's ctor → sets the static
        // ServiceProvider so the shim can resolve AuthorizerFor + the
        // security context per request. Equivalent to what
        // AuthorizationBehaviorState.ServiceProvider = sp would do via
        // internals, but goes through the public accessor.
        _ = sp.GetRequiredService<AuthorizationBehaviorAccessor>();
        return sp;
    }
}
```

**Step 2: Run the new tests**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release --filter "FullyQualifiedName~IntegrationTests"
```

Expected: 3 tests pass.

If `Pipeline_ordering_authorization_runs_before_later_behaviors` fails with `counter.Count == 1`, the pipeline-ordering invariant has actually regressed in ZeroAlloc.Mediator — STOP and investigate, do not patch the test.

If the allow-path test fails (`result` is not 14, or `counter.Count` is not 1), check that `IntegrationTestHandler` is registered (the generator should auto-discover it as the request's handler in the test-assembly compilation).

If the deny-path test throws something other than `AuthorizationDeniedException`, check that the shim correctly forwards (no try-catch around the inner call).

**Step 3: Run the full test suite once more**

```bash
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release
```

Expected: every test in the project passes, including the 3 new ones.

**Step 4: Commit**

```bash
git add tests/ZeroAlloc.Mediator.Authorization.Tests/IntegrationTests.cs
git commit -m "test(integration): cover pipeline ordering + IMediator.Send end-to-end

Three tests using the shim fixtures from the previous commit:

  - Pipeline_ordering_authorization_runs_before_later_behaviors (backlog #4):
    proves Order=-1000 short-circuits before Order=-500 fires.
  - End_to_end_through_IMediator_Send_allow_path (backlog #5):
    allow path returns handler's result through real dispatcher.
  - End_to_end_through_IMediator_Send_deny_path (backlog #5):
    deny path surfaces AuthorizationDeniedException without invoking
    the handler.

Comment documents the swap-test for verifying the ordering assertion is
meaningful: flip the counting behavior's Order to -2000 (before the shim)
and the test must fail with counter.Count == 1."
```

---

## Task 4: Refactor the AOT smoke to drop direct `AuthorizationBehaviorState` access

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`

**Step 1: Replace the six `AuthorizationBehaviorState.ServiceProvider = ...` lines**

Find each occurrence (six in total — lines 108, 119, 137, 150, 168 plus the gate setup). The current pattern is:

```csharp
AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
```

Replace each occurrence with:

```csharp
// Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
// AuthorizationBehaviorState.ServiceProvider as a side effect.
_ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
```

The behaviour is identical to direct field assignment because the accessor's
constructor does the same write. Importantly, no more `internal` access to
`AuthorizationBehaviorState` is required — both types are public after Task 1.

`AuthorizationBehaviorAccessor` was registered as a singleton by
`WithAuthorization()`, so the first resolution caches it; subsequent
`BuildProvider` calls return fresh providers and a fresh accessor each time
(the singleton is per-IServiceProvider, not per-process).

**Step 2: Drop the policy-only allocation gate (backlog #6)**

Locate `VerifyAllocationBudget` in `AuthorizedScenario.cs`. The method
currently ends with two `AllocationGate.AssertBudgetValueTask` calls. The
first measures `AuthorizationBehavior.Handle` (KEEP). The second measures
`policy.EvaluateAsync` (DELETE — this certifies ZeroAlloc.Authorization,
already certified upstream).

Delete the block:

```csharp
// Direct policy invocation — tightest budget (0 B/call) on v2's single-method
// IAuthorizationPolicy.EvaluateAsync returning UnitResult<AuthorizationFailure>.
IAuthorizationPolicy policy = new AotAdminPolicy();
AllocationGate.AssertBudgetValueTask(0, 1000,
    () => policy.EvaluateAsync(adminCtx),
    "Policy.EvaluateAsync (AOT smoke allow)");
```

Keep the preceding `AuthorizationBehavior.Handle` gate.

**Step 3: Verify the smoke still compiles**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet build samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release
```

Expected: SUCCEEDED, 0 errors. The smoke is still using the IVT to ZeroAlloc.Mediator.Authorization here (Task 5 removes it), so any other internal access (if any) still works at this point.

**Step 4: Run the smoke under .NET (JIT) before AOT publish**

```bash
dotnet run --project samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release
```

Expected: every scenario completes, including `[Authorization AotSmoke] OK`. The output should NOT include the dropped `Policy.EvaluateAsync (AOT smoke allow)` gate line; the remaining `AuthorizationBehavior.Handle (AOT smoke allow happy path)` line should print as before.

**Step 5: AOT publish and run**

```bash
dotnet publish samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release -p:PublishAot=true -o ./aot-out/AotSmoke
./aot-out/AotSmoke/ZeroAlloc.Mediator.AotSmoke
```

Expected: same console output as the JIT run; AOT compilation succeeded; allocation budgets pass.

If the Handle gate now allocates over budget, this is a new failure mode introduced by the refactor — STOP and inspect: confirm the `GetRequiredService<AuthorizationBehaviorAccessor>()` call is in setup, NOT inside the gated lambda (the gated lambda should still be the bare `AuthorizationBehavior.Handle<...>(...)` call).

**Step 6: Commit**

```bash
git add samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs
git commit -m "fix(aotsmoke): use AuthorizationBehaviorAccessor; drop policy-only gate

Two changes (backlog #6 + prep for #7):

  - Replace all six AuthorizationBehaviorState.ServiceProvider = ... lines
    with AuthorizationBehaviorAccessor resolution. Same side effect; goes
    through the public surface (Task 1) instead of InternalsVisibleTo.
  - Drop the policy.EvaluateAsync allocation gate from VerifyAllocationBudget.
    That gate certifies ZeroAlloc.Authorization (already certified in that
    repo's own smoke), not Mediator.Authorization. The remaining
    AuthorizationBehavior.Handle gate is the one that does this smoke's job.

AOT publish + run still passes — JIT and AOT both certify Handle's allocation
budget against the same 512 B / 1000-iter envelope."
```

---

## Task 5: Remove `InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"`

**Files:**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`

**Step 1: Delete the IVT line**

Open the csproj and find the `<ItemGroup>` containing the two `<InternalsVisibleTo>` entries. Delete the AotSmoke entry only:

Before:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Tests" />
  <InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />
</ItemGroup>
```

After:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Tests" />
</ItemGroup>
```

The Tests entry stays — `CountingDownstreamBehavior` in `TestFixtures.cs` reads `AuthorizationBehaviorState.ServiceProvider` (now public, so this could be relaxed) and `AuthorizationBehaviorTests`/`AllocationBudgetTests` still set the static for direct-call cases (still public, ditto). Both could in principle be flipped over to the accessor pattern in a follow-up, but doing it now bloats this PR. Tests keep the IVT for the moment.

**Step 2: Rebuild the smoke**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet build samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release
```

Expected: SUCCEEDED, 0 errors. If the build fails with CS0122 (inaccessible due to its protection level) on `AuthorizationBehaviorState` or `AuthorizationBehaviorAccessor`, those types are still `internal` in source — confirm Task 1 was committed and pushed correctly.

If any OTHER internal-only type from ZeroAlloc.Mediator.Authorization is referenced from the smoke that we haven't anticipated, the build will fail with CS0122 on that symbol. STOP and either (a) refactor the smoke to use a public alternative, or (b) promote the symbol to public via a follow-up PublicAPI change. Do NOT re-add the IVT silently.

**Step 3: Run the smoke (JIT)**

```bash
dotnet run --project samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release
```

Expected: same successful output as Task 4 Step 4.

**Step 4: AOT publish + run**

```bash
dotnet publish samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release -p:PublishAot=true -o ./aot-out/AotSmoke
./aot-out/AotSmoke/ZeroAlloc.Mediator.AotSmoke
```

Expected: same successful output. AOT publish must still succeed without warnings about trimmed-out symbols.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj
git commit -m "chore(authorization): drop InternalsVisibleTo \"ZeroAlloc.Mediator.AotSmoke\"

The smoke now uses AuthorizationBehaviorAccessor (resolved from DI) to
trigger the static-init side effect, instead of writing
AuthorizationBehaviorState.ServiceProvider via internals. Completes
backlog #7.

The InternalsVisibleTo to ZeroAlloc.Mediator.Authorization.Tests stays;
the test suite still has paths that touch internals (now-public types
are still accessed via the IVT-imported namespace, harmless), and
refactoring those is a separate cleanup."
```

---

## Task 6: Verify swap-test for the ordering assertion (manual; do NOT commit)

This validates that the `Pipeline_ordering_authorization_runs_before_later_behaviors` test catches an actual regression, not just any failure mode.

**Step 1: Temporarily change `CountingDownstreamBehavior`'s Order**

Open `tests/ZeroAlloc.Mediator.Authorization.Tests/TestFixtures.cs`. Find:

```csharp
[PipelineBehavior(Order = -500)]
public sealed class CountingDownstreamBehavior : IPipelineBehavior
```

Change to:

```csharp
[PipelineBehavior(Order = -2000)]
public sealed class CountingDownstreamBehavior : IPipelineBehavior
```

`-2000` is numerically BEFORE the shim's `-1000`, so the counter behavior should now fire ahead of authorization.

**Step 2: Run the ordering test**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release \
  --filter "FullyQualifiedName~IntegrationTests.Pipeline_ordering_authorization_runs_before_later_behaviors"
```

Expected: the test FAILS with `Assert.Equal(0, 1) - Actual: 1, Expected: 0`. This confirms the test's assertion meaningfully covers ordering — if a future refactor inverted the pipeline ordering, this test would catch it.

If the test PASSES with `-2000` (counter still 0), the assertion isn't actually testing ordering; investigate before reverting.

**Step 3: Revert**

Change `Order = -2000` back to `Order = -500`. Confirm:

```bash
git diff tests/ZeroAlloc.Mediator.Authorization.Tests/TestFixtures.cs
```

should show no remaining changes.

**Step 4: Run the full test suite to confirm reverted state**

```bash
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release
```

Expected: all tests pass.

**No commit for this task** — the swap-test is a one-time verification. The procedure is documented in the comment block at the top of `IntegrationTests` so a future maintainer can repeat it.

---

## Task 7: Push the branch and open the PR

**Step 1: Sanity check the commit history**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
git log --oneline origin/main..HEAD
```

Expected: design doc commit (`cf7ecd4`) + 5 implementation commits in this order:

1. `feat(authorization): promote AuthorizationBehaviorAccessor + State to public`
2. `test(fixtures): add integration-test shim + counting behavior`
3. `test(integration): cover pipeline ordering + IMediator.Send end-to-end`
4. `fix(aotsmoke): use AuthorizationBehaviorAccessor; drop policy-only gate`
5. `chore(authorization): drop InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"`

If any commit is missing or out of order, do `git rebase -i origin/main` BEFORE pushing.

**Step 2: Push the branch**

```bash
git push -u origin test/mediator-authorization-integration-tests
```

**Step 3: Open the PR**

```bash
gh pr create --title "test: Mediator.Authorization integration tests + AOT smoke restructure (backlog #4-#7)" --body "$(cat <<'EOF'
## Summary

Ships backlog items #4-#7 in one focused PR:

- **#4** — new `Pipeline_ordering_authorization_runs_before_later_behaviors` integration test that proves `AuthorizationBehavior`'s `Order=-1000` short-circuits before later behaviors fire. Swap-test verified (changing the downstream stub's Order to `-2000` makes this test fail with `counter.Count == 1`).
- **#5** — new `End_to_end_through_IMediator_Send_{allow,deny}_path` integration tests that exercise the real dispatcher → shim → real `AuthorizationBehavior` → `AuthorizerFor` → policy → handler chain.
- **#6** — AOT smoke drops the redundant `policy.EvaluateAsync` allocation gate (that certifies ZeroAlloc.Authorization, not Mediator.Authorization). The `AuthorizationBehavior.Handle` gate that does Mediator.Authorization's job stays.
- **#7** — `<InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />` removed from the Authorization csproj. The smoke now resolves the (newly-public) `AuthorizationBehaviorAccessor` from DI to trigger its static-init side effect.

The integration tests required a small additive PublicAPI change: `AuthorizationBehaviorAccessor` and `AuthorizationBehaviorState` promoted from `internal` to `public`. No signature changes, no removals.

## Why cross-assembly testing needed a shim

The Mediator source generator discovers `[PipelineBehavior]`-decorated behaviors only in the current compilation. `AuthorizationBehavior` lives in `ZeroAlloc.Mediator.Authorization` (a referenced assembly) and is invisible to the test assembly's generator. The new `AuthorizationBehaviorShim` (local to the test assembly) is a thin pass-through that the generator picks up; the shim forwards to `AuthorizationBehavior.Handle`. This is the only viable way to drive the real behavior through `IMediator.Send` from the test assembly.

## Design + plan

- Design: `docs/plans/2026-05-22-mediator-authorization-integration-tests-design.md`
- Plan: `docs/plans/2026-05-22-mediator-authorization-integration-tests.md`

## Test plan

- [ ] CI green: build + tests + aot-smoke
- [ ] AOT publish of the smoke still produces a working binary; allocation gates pass within budget
- [ ] Existing test suite still 100% green (the new pipeline behaviors only execute on `IMediator.Send`; no other test goes through the dispatcher)
- [ ] Swap-test for the ordering assertion verified manually pre-merge (documented inline)

## Followup

The test-assembly IVT to ZeroAlloc.Mediator.Authorization could be removed too, since the now-public `AuthorizationBehaviorState` covers the test fixture's needs. Left in for now to keep this PR focused on the smoke leak (#7); separate cleanup PR if/when needed.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 4: Watch CI**

```bash
gh pr checks --watch
```

Expected: build + aot-smoke + (any other) checks all pass.

If CI fails, diagnose via `gh run view <run-id> --log-failed`. Likely failure modes:

- `RS0016` / `RS0017` on PublicAPI.Unshipped.txt: fix the line text in Task 1's file per the analyzer's suggestion, amend Task 1's commit.
- AOT publish trim warning on a newly-public type: investigate; the promoted symbols should be trim-safe (they only touch `IServiceProvider`, no reflection).
- Pipeline-ordering test failing: indicates a real regression in ZeroAlloc.Mediator's pipeline ordering — file a separate issue, do not patch this test.

---

## Verification checklist (before merge)

- [ ] Task 1: `AuthorizationBehaviorAccessor` + `AuthorizationBehaviorState` public; PublicAPI.Unshipped.txt has both entries; build clean.
- [ ] Task 2: TestFixtures.cs has shim + counting behavior + integration types; existing tests still green.
- [ ] Task 3: 3 new integration tests in IntegrationTests.cs, all passing.
- [ ] Task 4: Smoke uses accessor; policy-only gate dropped; JIT + AOT runs succeed.
- [ ] Task 5: IVT to AotSmoke removed; smoke build + JIT + AOT still pass.
- [ ] Task 6: Swap-test verified (test fails with inverted Order, passes again after revert). No commit.
- [ ] Task 7: PR opened with green CI.

## Out of scope (documented in the design doc)

- Streaming-request authorization.
- OpenTelemetry on the deny path.
- Item #10 (org-wide AllocationGate factor-out) — deferred per its own graduation signal.
- Real `ZeroAlloc.Mediator.Validation` integration test — explicitly skipped during brainstorming.
- Removing the `InternalsVisibleTo "ZeroAlloc.Mediator.Authorization.Tests"` entry — separate followup.
