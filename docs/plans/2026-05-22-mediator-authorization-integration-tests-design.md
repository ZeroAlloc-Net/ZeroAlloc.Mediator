# Mediator.Authorization integration tests + AOT smoke restructure

Date: 2026-05-22
Status: Approved
Scope: backlog items #4 + #5 + #6 + #7

## Problem

`tests/ZeroAlloc.Mediator.Authorization.Tests/` covers `AuthorizationBehavior.Handle`
in unit-test form: each test constructs the behavior, supplies a mocked
`RequestHandlerDelegate next`, and asserts the policy/allow/deny logic. Two
integration paths are uncovered:

- **Pipeline ordering** (backlog #4). The behavior declares
  `[PipelineBehavior(Order = -1000)]` so authorization runs before
  validation / cache / logging. The constant is asserted in source, but no
  test proves the ordering takes effect end-to-end. A future refactor of
  Mediator's pipeline-ordering algorithm could break the assumed invariant
  invisibly.
- **End-to-end dispatch via `IMediator.Send`** (backlog #5). Today's tests
  bypass the generator-emitted `[ModuleInitializer]` wiring and the DI /
  dispatcher routing. The unit-level paths work; the integration is
  unverified.

Separately, the AOT smoke binary's allocation gate calls
`policy.EvaluateAsync(ctx)` directly — that measures `ZeroAlloc.Authorization`
(already certified upstream), not Mediator.Authorization's wiring. The
behavior's allocation profile under AOT is unverified (backlog #6). The
smoke accesses `AuthorizationBehaviorState.ServiceProvider` via
`<InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />`, a leak that
should disappear once the smoke uses the public surface only (backlog #7).

## Goals

- Add integration tests that prove pipeline ordering (deny short-circuits
  before later behaviors fire) and that `IMediator.Send` exercises the full
  wiring (allow + deny path).
- Restructure the AOT smoke to measure `AuthorizationBehavior.Handle`
  allocation instead of the underlying policy library.
- Remove `InternalsVisibleTo "ZeroAlloc.Mediator.AotSmoke"` from the
  Mediator.Authorization csproj.
- Stay scoped to Mediator.Authorization. Do not pull in
  `ZeroAlloc.Mediator.Validation` as a test dependency.

## Design

### Test fixture (`tests/ZeroAlloc.Mediator.Authorization.Tests/IntegrationTests.cs`)

**Critical constraint — cross-assembly pipeline-behavior wiring.** The Mediator source generator discovers pipeline behaviors by scanning `[PipelineBehavior]`-decorated types in the **current compilation only**. `AuthorizationBehavior` lives in `ZeroAlloc.Mediator.Authorization` (a separate assembly) and therefore is invisible to the generator running in the test assembly. The existing `AuthorizationBehaviorTests.cs` calls `AuthorizationBehavior.Handle` directly and explicitly opts out of `IMediator`-driven integration for this reason.

To run an `IMediator.Send` integration test, the test assembly needs a **local shim behavior** that the generator can see. The shim implements the marker `IPipelineBehavior` interface, carries the `[PipelineBehavior(Order = -1000)]` attribute, and its static `Handle<TRequest, TResponse>` method just forwards to `AuthorizationBehavior.Handle`. The pipeline-ordering counter behavior follows the same pattern (local class, `[PipelineBehavior(Order = -500)]`, static `Handle`).

`ZeroAlloc.Mediator` pipeline behaviors are NOT a generic-class shape (`IPipelineBehavior<TReq, TResp>`). The interface is a non-generic marker; behaviors are sealed classes with a STATIC `Handle<TRequest, TResponse>` generic method, decorated at class-level with `[PipelineBehavior(Order = N)]`.

Single new file. Shared types used by all three new tests:

```csharp
[Policy("IntegrationTestPolicy")]
public sealed class IntegrationTestPolicy : IAuthorizationPolicy
{
    // Allow when the security context carries the "Admin" role; deny otherwise.
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "needs Admin"));
}

[RequirePolicy("IntegrationTestPolicy")]
public sealed record IntegrationTestRequest(int Value) : IRequest<int>;

internal sealed class IntegrationTestHandler : IRequestHandler<IntegrationTestRequest, int>
{
    public ValueTask<int> Handle(IntegrationTestRequest r, CancellationToken ct)
        => ValueTask.FromResult(r.Value * 2);
}

// Local shim — the test-assembly's generator picks this up; the real
// AuthorizationBehavior is invisible to it because it lives in a referenced
// assembly. Same Order, identical contract, just forwards.
[PipelineBehavior(Order = -1000)]
public sealed class AuthorizationBehaviorShim : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        => AuthorizationBehavior.Handle<TRequest, TResponse>(request, ct, next);
}

// Ordering stub: numerically AFTER Authorization (-1000 < -500). Counter
// only ticks if this behavior actually runs in a given Send call.
internal sealed class InvocationCounter { public int Count; }

[PipelineBehavior(Order = -500)]
public sealed class CountingDownstreamBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        AuthorizationBehaviorState.ServiceProvider!
            .GetRequiredService<InvocationCounter>().Count++;
        return next(request, ct);
    }
}

internal sealed class TestSecurityContextAccessor : ISecurityContextAccessor
{
    public ISecurityContext Current { get; set; } = AnonymousSecurityContext.Instance;
}
```

The `ISecurityContextAccessor` shape matches the existing test fixture (`TestFixtures.cs`); each test scopes its own provider with the accessor's `Current` set to the desired security context.

The `CountingDownstreamBehavior` reads `AuthorizationBehaviorState.ServiceProvider` to resolve the counter — same trick as the existing tests (the test assembly has `InternalsVisibleTo`). This is acceptable in the test fixture; the IVT removal in #7 targets the smoke binary only.

### Test 1 — pipeline ordering (#4)

```csharp
[Fact]
public async Task Pipeline_ordering_authorization_runs_before_later_behaviors()
{
    var counter = new InvocationCounter();
    using var sp = BuildContainer(counter, AnonymousSecurityContext.Instance);
    var mediator = sp.GetRequiredService<IMediator>();

    // IntegrationTestRequest's policy needs the Admin role; anonymous denies →
    // AuthorizationDeniedException is thrown by the shim (forwards to real behavior).
    await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
        await mediator.Send(new IntegrationTestRequest(42)));

    // Proves ordering: downstream stub never ran because authorization
    // short-circuited at Order -1000 before -500 fired.
    Assert.Equal(0, counter.Count);
}
```

The stub at `Order = -500` is numerically AFTER Authorization's `-1000`. If
Authorization runs first AND short-circuits on deny, the stub never fires
→ `counter.Count == 0`. If a future change inverts the ordering, the stub
fires → assertion catches it.

**Swap-test for the assertion's meaningfulness:** temporarily changing
`CountingDownstreamBehavior`'s attribute to `[PipelineBehavior(Order = -2000)]`
(numerically before Authorization) must make this test FAIL with
`counter.Count == 1`. Verified manually pre-merge and documented as a
comment above the test class so a future maintainer can re-verify the
assertion is meaningful.

### Test 2 — end-to-end allow path (#5)

```csharp
[Fact]
public async Task End_to_end_through_IMediator_Send_allow_path()
{
    var counter = new InvocationCounter();
    using var sp = BuildContainer(counter, TestSecurityContexts.With("Admin"));
    var mediator = sp.GetRequiredService<IMediator>();

    var result = await mediator.Send(new IntegrationTestRequest(7));

    Assert.Equal(14, result);    // handler invoked: 7 * 2
    Assert.Equal(1, counter.Count);  // downstream stub also ran (allow path passes through)
}
```

### Test 3 — end-to-end deny path (#5)

```csharp
[Fact]
public async Task End_to_end_through_IMediator_Send_deny_path()
{
    var counter = new InvocationCounter();
    using var sp = BuildContainer(counter, AnonymousSecurityContext.Instance);
    var mediator = sp.GetRequiredService<IMediator>();

    await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
        await mediator.Send(new IntegrationTestRequest(7)));

    Assert.Equal(0, counter.Count);  // handler must NOT have run
}
```

(Test 3 is structurally similar to Test 1 — the difference is that Test 1's
purpose is to assert ordering; Test 3's purpose is to prove the full
dispatcher chain handles deny semantics. Keeping both makes each test's
intent explicit.)

### Shared container builder

```csharp
private static ServiceProvider BuildContainer(InvocationCounter counter, ISecurityContext ctx)
{
    var services = new ServiceCollection();
    services.AddZeroAllocAuthorization();
    services.AddSingleton(counter);
    services.AddScoped<ISecurityContextAccessor>(_ =>
        new TestSecurityContextAccessor { Current = ctx });
    services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
    return services.BuildServiceProvider();
}
```

The shim's static `Handle` reads `AuthorizationBehaviorState.ServiceProvider`,
which is set by `AuthorizationBehaviorAccessor` on first IServiceProvider
build — same production path the existing tests rely on. The test fixture
DOES use `InternalsVisibleTo` to access `AuthorizationBehaviorState` for
the `CountingDownstreamBehavior` (to resolve the counter); this is
acceptable in the test assembly. The IVT removal in item #7 targets the
sample binary only.

### AOT smoke restructure (#6)

**Current state.** `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`
already calls `AuthorizationBehavior.Handle<TRequest, TResponse>(...)` as a
static (the behavior IS a sealed class with static `Handle`; there is no
instance to construct). The `VerifyAllocationBudget` step measures
`AuthorizationBehavior.Handle` at line 171-174 with a 512 B budget. That
part of item #6 has already been completed during ongoing development.

What remains is the redundant **second** allocation gate at lines 176-181
that measures `policy.EvaluateAsync` directly:

```csharp
// Direct policy invocation — tightest budget (0 B/call) on v2's single-method
// IAuthorizationPolicy.EvaluateAsync returning UnitResult<AuthorizationFailure>.
IAuthorizationPolicy policy = new AotAdminPolicy();
AllocationGate.AssertBudgetValueTask(0, 1000,
    () => policy.EvaluateAsync(adminCtx),
    "Policy.EvaluateAsync (AOT smoke allow)");
```

This certifies `ZeroAlloc.Authorization` (already certified in that repo's
own AOT smoke), not Mediator.Authorization. Per backlog item #6's stated
purpose ("the AOT-side gate's job is to certify Mediator.Authorization's
runtime under the AOT runtime"), the policy gate is out of scope here and
should be removed.

**Change.** Delete lines 176-181 from `AuthorizedScenario.VerifyAllocationBudget`.
The Handle gate above stays unchanged.

**Allocation budget for the kept Handle gate.** Already at 512 B / 1000
iterations — set during the original gate addition; left alone unless the
post-IVT-removal change (next subsection) shifts the path.

### `InternalsVisibleTo` removal (#7)

**Why the IVT exists today.** `AuthorizedScenario.cs` sets
`AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider` directly
six times (lines 108, 119, 137, 150, 168). `AuthorizationBehaviorState` is
`internal`; access only works via the IVT.

**Why the static gets set in production.** `WithAuthorization()` registers
`AddSingleton(sp => new AuthorizationBehaviorAccessor(sp))`. The accessor's
constructor sets `AuthorizationBehaviorState.ServiceProvider = sp` as a
side effect. The singleton is lazy — nothing resolves it eagerly — so the
static stays null until some code path resolves `AuthorizationBehaviorAccessor`.
The AotSmoke (and the existing test suite) bypass this by setting the
static directly through the IVT.

For the smoke to drop the IVT cleanly, it needs to resolve the accessor
itself. The accessor is `internal`, so it must be promoted to `public`.

**Public API change.** Promote both:

```csharp
namespace ZeroAlloc.Mediator.Authorization;

// was: internal static class
public static class AuthorizationBehaviorState
{
    // was: internal static
    public static volatile IServiceProvider? ServiceProvider;
}

// was: internal sealed class
public sealed class AuthorizationBehaviorAccessor
{
    // was: internal
    public AuthorizationBehaviorAccessor(IServiceProvider serviceProvider) =>
        AuthorizationBehaviorState.ServiceProvider = serviceProvider;
}
```

Strictly additive PublicAPI change (no signature changes, no removals).
Update `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt`:

```
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorAccessor
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorAccessor.AuthorizationBehaviorAccessor(System.IServiceProvider! serviceProvider) -> void
ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorState
static ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorState.ServiceProvider -> System.IServiceProvider?
static ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorState.ServiceProvider.set -> void
```

(Exact lines mirror Roslyn's PublicApiAnalyzer-generated format.)

**Smoke refactor — replace the six direct assignments.** Each
`AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider` line
becomes:

```csharp
// Triggers AuthorizationBehaviorAccessor's ctor side-effect:
// sets AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider.
_ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
```

The accessor is registered as `AddSingleton` against the root provider, so
the first resolution caches it; subsequent scopes get the same instance.
The static reflects whichever provider was passed into the first ctor call
— for the smoke's use (one fresh `BuildProvider` per scenario), that's the
correct per-scenario provider.

**Then drop the IVT.** Delete from
`src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`:

```xml
<InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />
```

The `InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Tests"`
STAYS — the test fixture still uses the IVT for the
`CountingDownstreamBehavior`'s counter access (see Section 2). That IVT is
between two repo-internal projects; nothing leaks externally. Removing it
is a separate refactor not in scope for this work.

**Validation.** `dotnet build samples/ZeroAlloc.Mediator.AotSmoke/...` must
succeed after the smoke refactor + IVT removal. `dotnet publish
-p:PublishAot=true` on the smoke must also succeed and the allocation gates
must still pass within budget.

## Testing

- `dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests` shows 3 new
  passing tests on top of the existing suite.
- Manual swap-test on the ordering test verified the assertion catches the
  inverted-Order regression.
- `dotnet publish -p:PublishAot=true samples/ZeroAlloc.Mediator.AotSmoke`
  succeeds AND the new `authorized-*-handle` gates pass their budgets at
  runtime.
- `dotnet build` of the smoke csproj succeeds after the IVT removal.

## Out of scope

- Streaming-request authorization (backlog calls this out as deferred).
- OpenTelemetry on the deny path (deferred).
- Item #10 (org-wide AllocationGate factor-out) — deferred by its own
  graduation signal.
- Real `ZeroAlloc.Mediator.Validation` integration test — explicitly
  rejected during brainstorming (B over A on test approach): the stub
  approach decouples this test from the Validation package's behavior.
- Removing the `InternalsVisibleTo "ZeroAlloc.Mediator.Authorization.Tests"`
  entry. Tests still use the IVT for the counter behavior's
  `AuthorizationBehaviorState` access. The IVT to the smoke is the leak
  that matters; tests are first-party and stay coupled.
