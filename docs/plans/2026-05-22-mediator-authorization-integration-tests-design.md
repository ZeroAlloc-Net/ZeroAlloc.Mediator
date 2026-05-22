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

Single new file. Shared types used by all three new tests:

```csharp
[Policy("TestPolicy")]
public sealed class TestPolicy : IAuthorizationPolicy { ... }

[RequirePolicy("TestPolicy")]
public sealed record TestRequest(int Value) : IRequest<TestResponse>;
public sealed record TestResponse(int Echo);

internal sealed class TestHandler : IRequestHandler<TestRequest, TestResponse>
{
    public Task<TestResponse> Handle(TestRequest req, CancellationToken ct)
        => Task.FromResult(new TestResponse(req.Value));
}

internal sealed class FakeSecurityContextAccessor : ISecurityContextAccessor
{
    // AsyncLocal so future xUnit collection-level parallelism doesn't bleed
    // claims across tests. Set per-test via SetClaims(...).
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> _current = new();
    public ISecurityContext GetCurrent() => new TestSecurityContext(_current.Value ?? EmptyDict);
    public static void SetClaims(IReadOnlyDictionary<string, string> claims) => _current.Value = claims;
}

internal sealed class InvocationCounter { public int Count; }

[PipelineBehavior(Order = -500)] // numerically AFTER Authorization (-1000)
internal sealed class CountingDownstreamBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly InvocationCounter _counter;
    public CountingDownstreamBehavior(InvocationCounter counter) => _counter = counter;
    public Task<TResponse> Handle(TRequest req, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _counter.Count++;
        return next();
    }
}
```

### Test 1 — pipeline ordering (#4)

```csharp
[Fact]
public async Task Pipeline_ordering_authorization_runs_before_later_behaviors()
{
    var counter = new InvocationCounter();
    var sp = BuildContainer(services =>
    {
        services.AddSingleton(counter);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CountingDownstreamBehavior<,>));
    });

    FakeSecurityContextAccessor.SetClaims(EmptyDict); // no claims → TestPolicy denies
    var mediator = sp.GetRequiredService<IMediator>();
    var result = await mediator.Send(new TestRequest(42));

    Assert.Equal(0, counter.Count);  // proves ordering: downstream stub never ran
    Assert.True(IsAuthDenial(result));
}
```

The stub at `Order = -500` is numerically AFTER Authorization's `-1000`. If
Authorization runs first AND short-circuits on deny, the stub never fires
→ `counter.Count == 0`. If a future change inverts the ordering, the stub
fires → assertion catches it.

**Swap-test for the assertion's meaningfulness:** changing the stub's Order
to `-2000` (numerically before Authorization) must make this test FAIL with
`counter.Count == 1`. Verified manually pre-merge and documented as a
comment above the test.

### Test 2 — end-to-end allow path (#5)

```csharp
[Fact]
public async Task End_to_end_through_IMediator_Send_allow_path()
{
    var sp = BuildContainer(_ => { });
    FakeSecurityContextAccessor.SetClaims(new Dictionary<string, string> { ["sub"] = "test-user" });

    var mediator = sp.GetRequiredService<IMediator>();
    var result = await mediator.Send(new TestRequest(42));

    Assert.True(IsAllowedResult(result));
    Assert.Equal(42, ExtractValue(result));  // handler actually invoked
}
```

### Test 3 — end-to-end deny path (#5)

```csharp
[Fact]
public async Task End_to_end_through_IMediator_Send_deny_path()
{
    var sp = BuildContainer(_ => { });
    FakeSecurityContextAccessor.SetClaims(EmptyDict);

    var mediator = sp.GetRequiredService<IMediator>();
    var result = await mediator.Send(new TestRequest(42));

    Assert.True(IsAuthDenial(result));  // handler must NOT have run
}
```

### Shared container builder

```csharp
private static ServiceProvider BuildContainer(Action<IServiceCollection> extra)
{
    var services = new ServiceCollection();
    services.AddScoped<ISecurityContextAccessor, FakeSecurityContextAccessor>();
    services.AddSingleton<IRequestHandler<TestRequest, TestResponse>, TestHandler>();
    services.AddMediator().WithAuthorization(o => o.UseAccessor<FakeSecurityContextAccessor>());
    extra(services);
    return services.BuildServiceProvider();
}
```

The exact `IsAllowedResult` / `IsAuthDenial` / `ExtractValue` helpers mirror
the shape the existing `AuthorizationBehaviorTests` use (typed-failure
`Result<TResponse, AuthorizationFailure>` vs thrown denial), determined at
coding time so the new tests read consistently with the existing suite.

### AOT smoke restructure (#6)

`samples/ZeroAlloc.Mediator.AotSmoke/AuthorizedScenario.cs`:

```csharp
// Setup (outside any gate; setup cost is irrelevant to allocation budgets):
private readonly AuthorizationBehavior<TestRequest, TestResponse> _behavior;
private readonly TestRequest _allowRequest = new(1);
private readonly TestRequest _denyRequest = new(2);
private readonly TestResponse _okResponse = new(99);
private readonly RequestHandlerDelegate<TestResponse> _next;

ctor:
    _behavior = new AuthorizationBehavior<TestRequest, TestResponse>(/* policy registry, accessor */);
    _next = () => Task.FromResult(_okResponse);
    // Pre-build allow / deny security contexts so the gate measures Handle only.

// Inside the allocation gate (replaces today's policy.EvaluateAsync gate):
gate.Measure("authorized-allow-handle", () =>
    _behavior.Handle(_allowRequest, _next, ct: default).GetAwaiter().GetResult());
gate.Measure("authorized-deny-handle", () =>
    _behavior.Handle(_denyRequest, _next, ct: default).GetAwaiter().GetResult());
```

No DI container, no dispatcher, no `AuthorizationBehaviorState` access. Just
the behavior, the `RequestHandlerDelegate next`, and the security context.

**Allocation budgets:** start at the values used by the corresponding JIT
unit tests (`Behavior_*Allow_ZeroAllocation` / `Behavior_*Deny_ZeroAllocation`,
already passing). If the AOT runtime shows higher allocation than JIT, bump
the AOT budget with an inline comment noting the AOT-vs-JIT delta.

### `InternalsVisibleTo` removal (#7)

After the smoke refactor, delete from
`src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`:

```xml
<InternalsVisibleTo Include="ZeroAlloc.Mediator.AotSmoke" />
```

Validation: `dotnet build samples/ZeroAlloc.Mediator.AotSmoke/...` must
succeed cleanly. If it fails, the smoke still touches internals — STOP and
either make the symbol public (if safe) OR refactor the smoke. Do not
reintroduce the IVT silently.

If `PublicAPI.Unshipped.txt` references the removed visibility, drop those
lines.

### Constructor accessibility risk

If `AuthorizationBehavior<TRequest, TResponse>`'s constructor is `internal`
(plausible since today the smoke constructs via internals), the refactor in
Section 3 can't construct it directly without the IVT. In that case the fix
splits:

1. Promote the constructor to `public` (or add a `public static
   CreateForTesting(...)` factory).
2. Add the new symbol to `PublicAPI.Unshipped.txt`.
3. Only then drop the IVT.

This is a strictly additive PublicAPI change — no removals, no signature
changes — so the SemVer impact is none.

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
