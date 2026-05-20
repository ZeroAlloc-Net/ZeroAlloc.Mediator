# ZeroAlloc.Mediator.Authorization v2.0.0 — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Mediator.Authorization` v2.0.0 — split versioning from core, delete the in-package generator + static delegate plumbing, rewrite `AuthorizationBehavior` to consume `AuthorizerFor<TRequest>` via DI generic dispatch (the contract is now in `ZeroAlloc.Authorization` v2). User-facing API preserved; only internal plumbing changes.

**Architecture:** Additive-then-subtractive sequencing. First bump the contract dependency and rewrite the runtime to use the new pattern alongside the dead hooks; then rewrite tests against the new shape; then delete the now-dead generator + hooks; finally update admin files (PublicAPI, apicompat suppressions, release-please config). Every intermediate commit leaves the build green.

**Tech Stack:** C# / .NET 8 + 10 (multi-TFM: net8.0;net10.0), Roslyn `IIncrementalGenerator` (DELETED in this plan), `Microsoft.Extensions.DependencyInjection`, `ZeroAlloc.Authorization >= 2.0.0` (contract + bundled generator), `ZeroAlloc.Results` (`UnitResult<TError>`), release-please for SemVer 2.0.0 bump on the Authorization package only.

**Reference design:** [2026-05-19-mediator-authorization-v2-design.md](2026-05-19-mediator-authorization-v2-design.md)

**Repo root for all paths below:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/`

**Branch:** `feat/authorization-v2-split-versioning` (already created, HEAD `2c3e3fa` — the committed design doc).

---

## Pre-flight notes for the executor

- **OS:** Windows. Use PowerShell. **Bash is NOT available.**
- **SDK pin:** `global.json` pins `10.0.202` with `rollForward: latestMinor`. Locally `10.0.204` is installed → resolves fine, no workaround needed.
- **Conventional commits scope:** Every commit MUST use `(authorization)` scope, e.g. `feat(authorization)!: ...` or `chore(authorization): ...`. This is what release-please uses to bump ONLY the Authorization package (not core) once the config split lands.
- **Repo gotchas inherited from ZA.Authorization v2 execution:**
  - **ZA0601 analyzer** bans `Where()/Select()/ToArray()/FirstOrDefault()/Any()` LINQ calls inside loops in src code. Use manual `for`/`foreach`. (Test code may or may not be exempt — check on first failure.)
  - **`UnitResult<T>.Success()`** (generic-class-static-method form), NOT `UnitResult.Success<T>()`. Failures via bare `new AuthorizationFailure(code, reason)` — implicit conversion to `UnitResult<AuthorizationFailure>`.
  - **TreatWarningsAsErrors** is enabled; PublicAPI analyzer enforces `RS0016` / `RS0017` (missing/extra entries).
- **TDD discipline:** every task that touches runtime code has a failing-test step BEFORE implementation. Pre-existing tests act as the failing test for the rewrites.
- **One commit per task.**

---

## Tasks at a glance

| # | Task | Phase | Breaking? |
|---|---|---|---|
| 0 | Pre-flight: verify environment + branch state | — | — |
| 1 | Bump `ZeroAlloc.Authorization` PackageReference to `>= 2.0.0` in Authorization csproj | A (additive) | no |
| 2 | Rewrite `AuthorizationBehavior` to use `sp.GetService<AuthorizerFor<TRequest>>()` | A | yes (internal contract) |
| 3 | Slim `AuthorizationOptions` (drop `AutoRegisterDiscoveredPolicies` + `ValidatePoliciesAreRegistered`) | A | **yes** |
| 4 | Update `WithAuthorization()` extension (drop autoregister/validate, add D3 guard) | A | **yes** |
| 5 | Rewrite `AuthorizationBehaviorTests.cs` for new DI-based dispatch | A | no |
| 6 | Rewrite `WithAuthorizationTests.cs` + add 3 new guard tests | A | no |
| 7 | Rewrite `IAuthorizedRequestTests.cs` for new dispatch | A | no |
| 8 | Rewrite `AllocationBudgetTests.cs` for new dispatch | A | no |
| 9 | Rewrite `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs` | A | no |
| 10 | Delete `MediatorAuthorizationGeneratedHooks.cs` + its test | B (subtractive) | **yes** |
| 11 | Delete `src/ZeroAlloc.Mediator.Authorization.Generator/` (entire project) + slnx entry + ProjectReference | B | **yes** |
| 12 | Update `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt` (v2 surface) | B | yes |
| 13 | Update `apicompat-suppressions.xml` (the 3+ v2 breaks) | B | no |
| 14 | Update `release-please-config.json` for split versioning | C (admin) | no |
| 15 | Update `.release-please-manifest.json` (add Authorization @ 2.0.0) | C | no |
| 16 | End-to-end verification + push + open PR | — | — |

---

### Task 0: Pre-flight

**Step 1: Confirm branch + clean tree.**

```powershell
Set-Location c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
git branch --show-current
git status --short
```

Expected: branch `feat/authorization-v2-split-versioning`, clean tree (only untracked build outputs OK).

**Step 2: Confirm dotnet works + baseline build/test.**

```powershell
dotnet --version
dotnet build -c Release
dotnet test -c Release
```

Expected: `10.0.204` (or `10.0.300+` if user has it); build clean; capture test count for the Authorization test suite (`ZeroAlloc.Mediator.Authorization.Tests`) — every subsequent task should keep this count green or document the delta.

**Step 3: Note the file paths confirmed.**

The recon already confirmed:
- Source files at `src/ZeroAlloc.Mediator.Authorization/*.cs` (10 files including 2 to delete)
- Generator project at `src/ZeroAlloc.Mediator.Authorization.Generator/` (entire project to delete)
- Tests at `tests/ZeroAlloc.Mediator.Authorization.Tests/` (6 files, 1 to delete, 4 to rewrite)
- AOT smoke at `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`
- Admin files at repo root: `apicompat-suppressions.xml`, `release-please-config.json`, `.release-please-manifest.json`, `ZeroAlloc.Mediator.slnx`

No commit for Task 0.

---

### Task 1: Bump `ZeroAlloc.Authorization` PackageReference to `>= 2.0.0`

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`

**Step 1: Edit the version range.**

In the csproj, change line 11:
```xml
<PackageReference Include="ZeroAlloc.Authorization" Version="1.1.*" />
```
to:
```xml
<PackageReference Include="ZeroAlloc.Authorization" Version="2.*" />
```

(Floor on `2.0.0`, allows any 2.x. If `ZeroAlloc.Authorization` doesn't exist on nuget.org yet at 2.0.0, this restore will fail — but PR #19 was merged earlier today, so a v2.0.0 release should be published by release-please soon if not already. Verify availability via `nuget search ZeroAlloc.Authorization` or check nuget.org directly. If 2.x isn't published yet, STOP and report — the user may need to wait for the release pipeline.)

**Step 2: Restore + build.**

```powershell
dotnet restore
dotnet build -c Release
```

Expected: restore succeeds with ZA.Authorization 2.x; build initially **FAILS** with compile errors in `AuthorizationBehavior.cs` because:
- v2's `IAuthorizationPolicy` is async-only (4-method matrix is gone)
- Static `MediatorAuthorizationGeneratedHooks` still calls into the generator's `GeneratedAuthorizationLookup` which now emits against v2 attributes
- Various ripple effects

This is **expected**. Tasks 2–4 fix the compile errors by rewriting the runtime.

**Step 3: Commit (broken-build commit on purpose — recovered by next task).**

```powershell
git add src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj
git commit -m "build(authorization): pin ZeroAlloc.Authorization >= 2.0.0 as hard floor"
```

**Note:** Yes, this commits a broken build intentionally. Acceptable because (a) the broken state is fixed within the next 2-3 tasks, (b) the broken state is documented in the commit message, (c) the alternative — one mega-commit covering tasks 1-4 — sacrifices reviewability for atomicity.

If you want to avoid the broken-build commit, **squash tasks 1-4 into a single commit** at the end. Up to executor preference.

---

### Task 2: Rewrite `AuthorizationBehavior` to use `sp.GetService<AuthorizerFor<TRequest>>()`

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs`

**Step 1: Read the current behavior.**

```powershell
Get-Content src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs
```

Understand the current shape — particularly:
- The static `Handle<TRequest, TResponse>` method
- How it currently calls `MediatorAuthorizationGeneratedHooks.GetPoliciesForRequestType` and `MediatorAuthorizationGeneratedHooks.ResolvePolicy`
- The dual-path logic for `IRequest<T>` (throw on deny) vs `IAuthorizedRequest<T>` (Result on deny)

**Step 2: Rewrite.**

Replace the entire file contents with:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Pipeline behavior that authorizes a request via the source-generated
/// <see cref="AuthorizerFor{TRequest}"/> dispatcher (emitted by ZA.Authorization v2's
/// generator). Resolves an <see cref="ISecurityContext"/> from the configured source
/// in <see cref="AuthorizationOptions"/>, then calls the dispatcher's
/// <see cref="AuthorizerFor{TRequest}.EvaluateAsync"/>.
/// </summary>
/// <remarks>
/// Fail-open if no <c>AuthorizerFor&lt;TRequest&gt;</c> is registered for the request —
/// matches the <c>Mediator.Validation</c> pattern for the "no validator registered" case.
/// The compile-time <c>ZAUTH001</c> diagnostic (unknown policy name on <c>[RequirePolicy]</c>)
/// is the safety net for the realistic typo class. The <see cref="WithAuthorization"/>
/// startup guard catches the "forgot to call <c>services.AddZeroAllocAuthorization()</c>"
/// case at registration time.
/// </remarks>
internal static class AuthorizationBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        IServiceProvider sp,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
        where TRequest : notnull
    {
        // 1. Resolve the AuthorizerFor<TRequest> from DI; fail-open if absent.
        var authorizer = sp.GetService<AuthorizerFor<TRequest>>();
        if (authorizer is null)
        {
            return await next().ConfigureAwait(false);
        }

        // 2. Resolve ISecurityContext via the configured source.
        var options = sp.GetRequiredService<AuthorizationOptions>();
        var ctx = options.ResolveSecurityContext(sp);

        // 3. Evaluate.
        var result = await authorizer.EvaluateAsync(ctx, ct).ConfigureAwait(false);

        // 4. Dispatch on outcome.
        if (result.IsSuccess)
        {
            return await next().ConfigureAwait(false);
        }

        // Deny: pick path based on TResponse shape.
        var failure = result.Error;
        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<,>))
        {
            // IAuthorizedRequest<T> path — return Result<T, AuthorizationFailure>.Failure(...)
            return AuthorizationFailureFactory.CreateFailureResult<TResponse>(failure);
        }

        // Plain IRequest<T> path — throw.
        throw new AuthorizationDeniedException(failure.Code, failure.Reason);
    }
}
```

(Note: `RequestHandlerDelegate<TResponse>` is the Mediator core's delegate type for the pipeline `next()` call. Confirm the exact signature by reading any other behavior in the repo that uses it — e.g., `ZeroAlloc.Mediator.Validation`'s `ValidationBehavior.cs`. If the signature differs, adjust.)

`AuthorizationFailureFactory` is an existing helper file (`src/ZeroAlloc.Mediator.Authorization/AuthorizationFailureFactory.cs`); it likely has a `CreateFailureResult<TResponse>` method that boxes the result-type Failure. If not, it needs a small addition — verify by reading the file first.

**Step 3: Build.**

```powershell
dotnet build -c Release
```

May still fail due to dead code in `MediatorAuthorizationGeneratedHooks.cs` (its delegate types reference the old `IAuthorizationPolicy` 4-method contract). That's expected — Tasks 3–4 fix the rest of the runtime; Task 10 deletes the hooks file entirely.

**If the build fails ONLY in the hooks file or its tests:** acceptable interim state; proceed.

**If the build fails elsewhere:** investigate; the rewrite may be missing something.

**Step 4: Commit.**

```powershell
git add src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs
git commit -m "feat(authorization)!: rewrite AuthorizationBehavior to consume AuthorizerFor<T> via DI

BREAKING CHANGE: AuthorizationBehavior no longer reads policies via the
static MediatorAuthorizationGeneratedHooks delegate plumbing. It now
resolves AuthorizerFor<TRequest> from the IServiceProvider — the source
generator in ZeroAlloc.Authorization v2.0.0 emits one per
[RequirePolicy]-decorated request type."
```

---

### Task 3: Slim `AuthorizationOptions`

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization/AuthorizationOptions.cs`

**Step 1: Read current shape.**

```powershell
Get-Content src/ZeroAlloc.Mediator.Authorization/AuthorizationOptions.cs
```

Identify and DELETE:
- `AutoRegisterDiscoveredPolicies` property (default `true`)
- `ValidatePoliciesAreRegistered()` method
- Any private state or helper code supporting just those two surface items

Identify and KEEP:
- `UseSecurityContextFactory(Func<IServiceProvider, ISecurityContext>)` configuration method
- `UseAnonymousSecurityContext()` configuration method
- `UseAccessor<TAccessor>()` configuration method (where `TAccessor : ISecurityContextAccessor`)
- The internal `ResolveSecurityContext(IServiceProvider)` method called by `AuthorizationBehavior`
- The "exactly-one-configure-method-must-be-called" validation logic

**Step 2: Apply edits.**

Make the deletions. The final file should contain only security-context-source configuration. No policy-registry concerns remain.

**Step 3: Build.**

```powershell
dotnet build -c Release
```

Build may still fail because `WithAuthorization()` extension calls the dropped methods. Task 4 fixes that.

**Step 4: Commit.**

```powershell
git add src/ZeroAlloc.Mediator.Authorization/AuthorizationOptions.cs
git commit -m "feat(authorization)!: drop AutoRegisterDiscoveredPolicies + ValidatePoliciesAreRegistered

BREAKING CHANGE: AuthorizationOptions no longer exposes the
AutoRegisterDiscoveredPolicies property or ValidatePoliciesAreRegistered
method. Policy registration is now handled by the source generator in
ZeroAlloc.Authorization v2.0.0 via AddZeroAllocAuthorization(). DI
surfaces missing-registration errors lazily on first use, replacing the
v1 eager startup check."
```

---

### Task 4: Update `WithAuthorization()` extension (drop autoregister/validate, add D3 guard)

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationServiceCollectionExtensions.cs`

**Step 1: Read current shape.**

Identify and DELETE the lines that:
- Call `MediatorAuthorizationGeneratedHooks.RegisterDiscoveredPolicies?.Invoke(services)`
- Call `options.ValidatePoliciesAreRegistered()`
- Read/write `AutoRegisterDiscoveredPolicies` flag

Identify and KEEP:
- The `WithAuthorization(this IMediatorBuilder builder, Action<AuthorizationOptions> configure)` signature
- The idempotency guard via `AuthorizationBehaviorAccessor` type check
- The configure-callback invocation
- The pipeline-behavior registration at order `-1000`

**Step 2: Add the D3 guard.**

After the `configure(options)` invocation completes (so the configure validation has already run for the security-context-source case) AND before the pipeline-behavior registration, insert:

```csharp
// D3 guard: ensure services.AddZeroAllocAuthorization() was called first.
// Walks the registered ServiceDescriptors looking for any AuthorizerFor<>.
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

**Add an `using ZeroAlloc.Authorization;`** at the top of the file so `AuthorizerFor<>` resolves.

**Step 3: Build.**

```powershell
dotnet build -c Release
```

Expected: still failing in `MediatorAuthorizationGeneratedHooks.cs` (the only remaining compile error site). Task 10 deletes that file. Move on.

If errors appear in test files (the static hooks tests), that's expected too — Task 5+ rewrites them.

**Step 4: Commit.**

```powershell
git add src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationServiceCollectionExtensions.cs
git commit -m "feat(authorization)!: simplify WithAuthorization + add D3 missing-registration guard

BREAKING CHANGE: WithAuthorization() no longer triggers the v1
auto-register + eager-validate flow (handled by
ZeroAlloc.Authorization's AddZeroAllocAuthorization() in v2). Adds a
startup-time guard that throws InvalidOperationException with an
actionable message if AddZeroAllocAuthorization() was forgotten or
called after AddMediator()."
```

---

### Tasks 5–8: Rewrite tests against the new DI-based dispatch

Same pattern for each test file: read, rewrite, run, commit. Use the v2 attributes (`[Policy]`, `[RequirePolicy]`) and the simplified `IAuthorizationPolicy.EvaluateAsync(ISecurityContext, CancellationToken)` contract everywhere.

#### Task 5: `AuthorizationBehaviorTests.cs`

**Files:**
- Modify: `tests/ZeroAlloc.Mediator.Authorization.Tests/AuthorizationBehaviorTests.cs`

Replace v1 test policies (which implemented 4-method `IAuthorizationPolicy`) with v2 async-only policies. Replace v1 `[AuthorizationPolicy]` / `[Authorize]` attributes with v2 `[Policy]` / `[RequirePolicy]`. Wire `AddZeroAllocAuthorization()` into every test's DI setup.

Test cases per Section 5 of the design doc:
- `Allow_FlowsThroughToHandler`
- `Deny_OnIRequest_ThrowsAuthorizationDeniedException`
- `Deny_OnIAuthorizedRequest_ReturnsFailureResult`
- `MultiplePolicies_AndSemantics_FirstDenyShortCircuits`
- `NoAuthorizerForRequest_FailsOpen`
- `PolicyResolution_ScopedPerRequest`
- `Cancellation_Propagates`

A sample sync-completing policy in v2:

```csharp
[Policy("admin")]
public sealed class AdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}

[RequirePolicy("admin")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;
```

DI setup:

```csharp
var services = new ServiceCollection();
services.AddZeroAllocAuthorization();              // emitted by ZA.Authorization generator
services.AddMediator(b => b.WithAuthorization(auth => auth.UseAnonymousSecurityContext()));
// (or UseAccessor<TestAccessor> for tests that customize the security context)
```

**Run + verify GREEN:**

```powershell
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ --filter "FullyQualifiedName~AuthorizationBehaviorTests" -c Release
```

**Commit:**

```powershell
git add tests/ZeroAlloc.Mediator.Authorization.Tests/AuthorizationBehaviorTests.cs
git commit -m "test(authorization): rewrite AuthorizationBehaviorTests for DI-based dispatch"
```

#### Task 6: `WithAuthorizationTests.cs` + 3 NEW guard tests

**Files:**
- Modify: `tests/ZeroAlloc.Mediator.Authorization.Tests/WithAuthorizationTests.cs`

Existing tests to keep (rewritten against v2):
- `RequiresSecurityContextSource_ThrowsInvalidOperation`
- `MultipleSecurityContextSources_ThrowsInvalidOperation`
- `Idempotency_DoubleRegistration_NoOps`

NEW tests (D3 guard):
- `WithoutAddZeroAllocAuthorization_ThrowsClearError` — call `services.AddMediator(b => b.WithAuthorization(auth => auth.UseAnonymousSecurityContext()))` WITHOUT a preceding `services.AddZeroAllocAuthorization()`. Assert `InvalidOperationException` thrown with message containing `"services.AddZeroAllocAuthorization()"`.
- `WithAddZeroAllocAuthorization_FirstSucceeds` — correct order; no throw; assert `AuthorizationBehavior` registered.
- `WithAddZeroAllocAuthorization_WrongOrder_Throws` — call `AddMediator` BEFORE `AddZeroAllocAuthorization`. Assert same `InvalidOperationException`.

Sample guard test:

```csharp
[Fact]
public void WithoutAddZeroAllocAuthorization_ThrowsClearError()
{
    var services = new ServiceCollection();
    // NO services.AddZeroAllocAuthorization();

    var ex = Assert.Throws<InvalidOperationException>(() =>
        services.AddMediator(b => b.WithAuthorization(auth => auth.UseAnonymousSecurityContext())));

    Assert.Contains("services.AddZeroAllocAuthorization()", ex.Message, StringComparison.Ordinal);
    Assert.Contains("before 'services.AddMediator(...)'", ex.Message, StringComparison.Ordinal);
}
```

**Run + GREEN + commit:**

```powershell
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ --filter "FullyQualifiedName~WithAuthorizationTests" -c Release
git add tests/ZeroAlloc.Mediator.Authorization.Tests/WithAuthorizationTests.cs
git commit -m "test(authorization): rewrite WithAuthorizationTests + add 3 D3 guard tests"
```

#### Task 7: `IAuthorizedRequestTests.cs`

**Files:**
- Modify: `tests/ZeroAlloc.Mediator.Authorization.Tests/IAuthorizedRequestTests.cs`

Cover the `IAuthorizedRequest<TPayload>` Result-returning path. Use v2 attributes + the new dispatch. Key tests:
- `IAuthorizedRequest_DenyReturnsFailureResult_NotThrow`
- `IAuthorizedRequest_AllowReturnsSuccessResultWithPayload`
- `IAuthorizedRequest_FailureRoundTripsCodeAndReason`

**Run + GREEN + commit:**

```powershell
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ --filter "FullyQualifiedName~IAuthorizedRequestTests" -c Release
git add tests/ZeroAlloc.Mediator.Authorization.Tests/IAuthorizedRequestTests.cs
git commit -m "test(authorization): rewrite IAuthorizedRequestTests for DI-based dispatch"
```

#### Task 8: `AllocationBudgetTests.cs`

**Files:**
- Modify: `tests/ZeroAlloc.Mediator.Authorization.Tests/AllocationBudgetTests.cs`

Two zero-alloc scenarios under `AllocationGate.AssertBudgetValueTask(maxBytes: 0)`:
- `Allow_HappyPath_ZeroAlloc`
- `IAuthorizedRequest_DenyPath_ZeroAlloc`

The `IRequest<T>`-throw path is documented but NOT gated (exception throwing allocates legitimately).

**Run + GREEN + commit:**

```powershell
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ --filter "FullyQualifiedName~AllocationBudgetTests" -c Release
git add tests/ZeroAlloc.Mediator.Authorization.Tests/AllocationBudgetTests.cs
git commit -m "test(authorization): rewrite AllocationBudgetTests for DI-based dispatch"
```

#### After Tasks 5-8: Confirm full test suite green

```powershell
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release
```

Expected: ALL tests passing EXCEPT the still-existing `MediatorAuthorizationGeneratedHooksTests.cs` (which Task 10 deletes). If those tests are failing because they reference removed types — that's fine, they're slated for deletion. If they're hiding other failures, address first.

---

### Task 9: Rewrite `AuthorizedScenario.cs` (AOT smoke binary)

**Files:**
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`

**Step 1: Read current shape** — understand the scenarios covered (throw path, Result path, allow + anonymous-deny, AllocationGate budget validation).

**Step 2: Rewrite.**

Drop any direct manipulation of `AuthorizationBehaviorState.ServiceProvider` (deleted state). New shape:

```csharp
public static void Run()
{
    var services = new ServiceCollection();
    services.AddZeroAllocAuthorization();
    services.AddMediator(b => b.WithAuthorization(auth => auth.UseAnonymousSecurityContext()));
    using var sp = services.BuildServiceProvider();

    using var scope = sp.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

    // Scenario A: throw path on IRequest<T>
    AllocationGate.AssertBudgetValueTask(maxBytes: 0, async () =>
    {
        // (request setup, .Send call, assert outcome)
    });

    // Scenario B: Result path on IAuthorizedRequest<T>
    AllocationGate.AssertBudgetValueTask(maxBytes: 0, async () =>
    {
        // (request setup, .Send call, assert outcome)
    });

    Console.WriteLine("[Authorization AotSmoke] OK");
}
```

Declare the test policy + requests using v2 attributes (`[Policy("aot")]`, `[RequirePolicy("aot")]`).

**Step 3: AOT publish + run.**

```powershell
$proj = "samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj"
dotnet publish $proj -c Release -r win-x64 -p:PublishAot=true
$exe = Get-ChildItem samples/ZeroAlloc.Mediator.AotSmoke/bin/Release/net10.0/win-x64/publish/*.exe | Select-Object -First 1
& $exe.FullName
```

Expected: exit 0, output includes "OK", 0 bytes allocated on the happy path.

**If AOT publish fails (e.g., NETSDK1207 for ProjectReference into a netstandard2.0 generator)** — note that this is fixed by deleting the generator ProjectReference in Task 11. Move on; this scenario will be re-verified in Task 16.

**Step 4: Commit.**

```powershell
git add samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs
git commit -m "test(authorization): rewrite AotSmoke AuthorizedScenario for new DI dispatch"
```

---

### Task 10: Delete `MediatorAuthorizationGeneratedHooks.cs` + its test

**Files:**
- Delete: `src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationGeneratedHooks.cs`
- Delete: `tests/ZeroAlloc.Mediator.Authorization.Tests/MediatorAuthorizationGeneratedHooksTests.cs`

**Step 1: Delete the files.**

```powershell
Remove-Item src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationGeneratedHooks.cs
Remove-Item tests/ZeroAlloc.Mediator.Authorization.Tests/MediatorAuthorizationGeneratedHooksTests.cs
```

**Step 2: Build + test.**

```powershell
dotnet build -c Release
dotnet test -c Release
```

Build may still fail because the Generator project's `LookupEmitter` writes a `[ModuleInitializer]` that references `MediatorAuthorizationGeneratedHooks`. That code is generated INTO the consuming assembly at compile time. With the hooks deleted, the consuming compile fails because the generator emits a reference to a non-existent type.

**This is expected** — Task 11 deletes the generator project itself, which removes the module-initializer emission.

If you need the build to stay green between tasks 10 and 11, **squash them**. Recommended: do not squash; the two deletions are logically distinct (hook plumbing vs the generator that wrote the hook wire-up).

**Step 3: Commit.**

```powershell
git add -A src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationGeneratedHooks.cs tests/ZeroAlloc.Mediator.Authorization.Tests/MediatorAuthorizationGeneratedHooksTests.cs
git commit -m "refactor(authorization)!: delete MediatorAuthorizationGeneratedHooks + its tests

BREAKING CHANGE: The static MediatorAuthorizationGeneratedHooks class
and its delegate plumbing are removed. AuthorizationBehavior now
resolves AuthorizerFor<TRequest> from the IServiceProvider directly
(see prior commit). No consumer ever interacted with this class
directly — it was generator-emitted internal state."
```

---

### Task 11: Delete generator project + slnx entry + ProjectReference

**Files:**
- Delete (directory): `src/ZeroAlloc.Mediator.Authorization.Generator/`
- Modify: `ZeroAlloc.Mediator.slnx` (remove the project entry)
- Modify: `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj` (remove the Generator `<ProjectReference>` + Generator.Tests `<InternalsVisibleTo>`)

**Step 1: Delete the generator directory.**

```powershell
Remove-Item -Recurse -Force src/ZeroAlloc.Mediator.Authorization.Generator
```

**Step 2: Remove the slnx entry.**

Edit `ZeroAlloc.Mediator.slnx` and delete the line referencing `ZeroAlloc.Mediator.Authorization.Generator.csproj`. Pattern probably looks like:

```xml
<Project Path="src/ZeroAlloc.Mediator.Authorization.Generator/ZeroAlloc.Mediator.Authorization.Generator.csproj" />
```

**Step 3: Remove the ProjectReference from Authorization csproj.**

In `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`, delete these two lines (around lines 13-14 and the related InternalsVisibleTo around line 19):

```xml
<ProjectReference Include="..\ZeroAlloc.Mediator.Authorization.Generator\ZeroAlloc.Mediator.Authorization.Generator.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

And:
```xml
<InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Generator.Tests" />
```

**Step 4: Build + test.**

```powershell
dotnet build -c Release
dotnet test -c Release
```

Expected: NOW everything is green. Build clean. All Authorization tests pass. The runtime uses the new DI-based dispatch end-to-end.

**Step 5: Commit.**

```powershell
git add -A src/ZeroAlloc.Mediator.Authorization.Generator/ ZeroAlloc.Mediator.slnx src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj
git commit -m "refactor(authorization)!: delete in-package source generator (lifted to ZA.Authorization v2)

BREAKING CHANGE: ZeroAlloc.Mediator.Authorization.Generator project is
removed. Policy discovery and AuthorizerFor<T> emission are now
performed by the bundled generator inside ZeroAlloc.Authorization
v2.0.0 (lifted to the contract repo so any framework, not just Mediator,
can consume the same registry pattern)."
```

---

### Task 12: Update `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt`

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Shipped.txt`
- Modify: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt`

**Step 1: Build first to see what RS0016/RS0017 says.**

```powershell
dotnet build -c Release
```

If there are RS0016 (missing entry) or RS0017 (extra entry) errors, those are your guide. Update Shipped/Unshipped to match the v2 surface.

**Step 2: Move surviving public symbols to Shipped; mark removed ones via `*REMOVED*` in Unshipped.**

Symbols REMOVED from public surface in v2:
- `ZeroAlloc.Mediator.Authorization.MediatorAuthorizationGeneratedHooks` (entire class)
- `ZeroAlloc.Mediator.Authorization.AuthorizationOptions.AutoRegisterDiscoveredPolicies.get` (and `.set` if present)
- `ZeroAlloc.Mediator.Authorization.AuthorizationOptions.ValidatePoliciesAreRegistered()`

Possibly also (verify by inspecting v1 Shipped):
- Helper types from `MediatorAuthorizationGeneratedHooks` (if any were public)

For each removed symbol, add a line to `PublicAPI.Unshipped.txt`:
```
*REMOVED*ZeroAlloc.Mediator.Authorization.MediatorAuthorizationGeneratedHooks
*REMOVED*ZeroAlloc.Mediator.Authorization.AuthorizationOptions.AutoRegisterDiscoveredPolicies.get -> bool
*REMOVED*ZeroAlloc.Mediator.Authorization.AuthorizationOptions.AutoRegisterDiscoveredPolicies.set -> void
*REMOVED*ZeroAlloc.Mediator.Authorization.AuthorizationOptions.ValidatePoliciesAreRegistered() -> void
```

And remove the same lines from `PublicAPI.Shipped.txt`.

**Step 3: Build to verify clean.**

```powershell
dotnet build -c Release
```

Expected: 0 errors, in particular no RS0016/RS0017.

**Step 4: Commit.**

```powershell
git add src/ZeroAlloc.Mediator.Authorization/PublicAPI.*.txt
git commit -m "chore(authorization): update PublicAPI for v2 surface"
```

---

### Task 13: Update `apicompat-suppressions.xml` for v2 breaks

**Files:**
- Modify: `apicompat-suppressions.xml` (at repo root)

**Step 1: Read current contents** and note the existing structure (the core's v3.0.0 break entry).

**Step 2: Append v2 entries for the Authorization breaks.**

Pattern follows the one shipped in ZA.Authorization PR #19: each break gets per-TFM entries with `IsBaselineSuppression=true` + explicit `Left`/`Right` paths.

For TFMs `net8.0` AND `net10.0` (Mediator.Authorization is multi-TFM), add entries for:

```xml
<!-- ZA.Mediator.Authorization v2.0.0 - MediatorAuthorizationGeneratedHooks removal -->
<Suppression>
  <DiagnosticId>CP0001</DiagnosticId>
  <Target>T:ZeroAlloc.Mediator.Authorization.MediatorAuthorizationGeneratedHooks</Target>
  <Left>lib/net8.0/ZeroAlloc.Mediator.Authorization.dll</Left>
  <Right>lib/net8.0/ZeroAlloc.Mediator.Authorization.dll</Right>
  <IsBaselineSuppression>true</IsBaselineSuppression>
  <Justification>Removed in v2.0.0 — internal generator-emitted plumbing replaced by DI generic dispatch via AuthorizerFor&lt;T&gt;.</Justification>
</Suppression>
<Suppression>
  <DiagnosticId>CP0001</DiagnosticId>
  <Target>T:ZeroAlloc.Mediator.Authorization.MediatorAuthorizationGeneratedHooks</Target>
  <Left>lib/net10.0/ZeroAlloc.Mediator.Authorization.dll</Left>
  <Right>lib/net10.0/ZeroAlloc.Mediator.Authorization.dll</Right>
  <IsBaselineSuppression>true</IsBaselineSuppression>
  <Justification>Removed in v2.0.0 — internal generator-emitted plumbing replaced by DI generic dispatch via AuthorizerFor&lt;T&gt;.</Justification>
</Suppression>

<!-- ZA.Mediator.Authorization v2.0.0 - AuthorizationOptions.AutoRegisterDiscoveredPolicies removal -->
<Suppression>
  <DiagnosticId>CP0002</DiagnosticId>
  <Target>M:ZeroAlloc.Mediator.Authorization.AuthorizationOptions.AutoRegisterDiscoveredPolicies.get</Target>
  <Left>lib/net8.0/ZeroAlloc.Mediator.Authorization.dll</Left>
  <Right>lib/net8.0/ZeroAlloc.Mediator.Authorization.dll</Right>
  <IsBaselineSuppression>true</IsBaselineSuppression>
  <Justification>Removed in v2.0.0 — policy auto-registration now handled by ZeroAlloc.Authorization v2's AddZeroAllocAuthorization() extension.</Justification>
</Suppression>
<!-- repeat for net10.0 -->
<!-- repeat for .set if it was public -->
<!-- repeat for ValidatePoliciesAreRegistered() -->
```

Audit during implementation: check whether any other types from the deleted Generator project were `public` and need additional CP0001 entries.

**Step 3: Verify the file is well-formed XML.**

```powershell
[xml](Get-Content apicompat-suppressions.xml -Raw) | Out-Null
"XML parsed OK"
```

**Step 4: Commit.**

```powershell
git add apicompat-suppressions.xml
git commit -m "build(authorization): suppress intentional v2 breaking-API diagnostics"
```

---

### Task 14: Update `release-please-config.json` for split versioning

**Files:**
- Modify: `release-please-config.json`

**Step 1: Read current config** (we already did during recon — single package at `.` with `release-type: simple`).

**Step 2: Rewrite to multi-package config.**

```jsonc
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "packages": {
    ".": {
      "release-type": "simple",
      "package-name": "ZeroAlloc.Mediator",
      "component": "mediator",
      "include-component-in-tag": false
    },
    "src/ZeroAlloc.Mediator.Authorization": {
      "release-type": "simple",
      "package-name": "ZeroAlloc.Mediator.Authorization",
      "component": "authorization",
      "include-component-in-tag": true,
      "initial-version": "2.0.0"
    }
  },
  "changelog-sections": [
    { "type": "feat", "section": "Features" },
    { "type": "fix",  "section": "Bug Fixes" },
    { "type": "perf", "section": "Performance Improvements" },
    { "type": "refactor", "section": "Code Refactoring" },
    { "type": "docs", "section": "Documentation" },
    { "type": "test", "section": "Tests" },
    { "type": "deps", "section": "Dependencies" },
    { "type": "build", "section": "Build System", "hidden": true },
    { "type": "ci",   "section": "Continuous Integration", "hidden": true },
    { "type": "chore", "section": "Miscellaneous", "hidden": true }
  ]
}
```

Key decisions baked in:
- **Core stays without component-in-tag** (`include-component-in-tag: false`) so existing tags `v4.1.x` keep their format — no breaking change to the core's release history.
- **Authorization gets component-in-tag** (`include-component-in-tag: true`) so its tags become `authorization-v2.0.0` etc., distinguishable from core tags.
- **Authorization scope** picks up commits matching `feat(authorization)` etc. (release-please supports scoped conventional commits automatically).
- **`initial-version: 2.0.0`** is the starting point for the Authorization package's own release line.

**Step 3: Verify JSON well-formed.**

```powershell
Get-Content release-please-config.json | ConvertFrom-Json | Out-Null
"JSON parsed OK"
```

**Step 4: Commit.**

```powershell
git add release-please-config.json
git commit -m "ci(authorization): split release-please config — Authorization versions independently from v2.0.0"
```

---

### Task 15: Update `.release-please-manifest.json` (add Authorization @ 2.0.0)

**Files:**
- Modify: `.release-please-manifest.json`

**Step 1: Rewrite.**

```jsonc
{
  ".": "4.1.4",
  "src/ZeroAlloc.Mediator.Authorization": "2.0.0"
}
```

(The core's version may have advanced since the recon — verify with `Get-Content .release-please-manifest.json` before edit and preserve whatever core value is current. The Authorization key is the new addition.)

**Step 2: Commit.**

```powershell
git add .release-please-manifest.json
git commit -m "ci(authorization): manifest entry for Authorization @ 2.0.0"
```

---

### Task 16: End-to-end verification + push + open PR

**Step 1: Clean rebuild + full test sweep.**

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

Expected: 0 errors, all Mediator + Mediator.Authorization tests pass. Capture the count delta (we deleted 1 test file with N tests, rewrote 4 files, added 3 new guard tests).

**Step 2: AOT smoke re-verification.**

```powershell
$proj = "samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj"
dotnet publish $proj -c Release -r win-x64 -p:PublishAot=true
$exe = Get-ChildItem samples/ZeroAlloc.Mediator.AotSmoke/bin/Release/net10.0/win-x64/publish/*.exe | Select-Object -First 1
& $exe.FullName
```

Expected: exit 0; output includes the Authorization scenario's "OK" line.

**Step 3: Verify pack produces correct package + version.**

```powershell
dotnet pack src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj -c Release -o artifacts/local
Get-ChildItem artifacts/local/ZeroAlloc.Mediator.Authorization.*.nupkg | Select-Object Name
```

Expected: `ZeroAlloc.Mediator.Authorization.0.0.0-local.nupkg` (the csproj `<Version>` placeholder; release-please overrides on actual release). Verify metadata via:

```powershell
$nupkg = Get-ChildItem artifacts/local/ZeroAlloc.Mediator.Authorization.*.nupkg | Select-Object -First 1
$tmp = Join-Path $env:TEMP "za-mauth-pack-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $tmp | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg.FullName, $tmp)
Get-Content (Join-Path $tmp "*.nuspec") | Select-String "ZeroAlloc.Authorization"
Remove-Item -Recurse -Force $tmp
```

Expected: nuspec contains a `<dependency id="ZeroAlloc.Authorization" version="2.0.0" ...>` entry (or similar `2.*` range — depends on how the version pin was written in Task 1).

**Step 4: Branch diff inspection.**

```powershell
git log --oneline main..HEAD
git diff main..HEAD --stat
```

Expected ~16 commits, all with `(authorization)` scope (plus the docs design commit from Task 0). Files-changed summary should match:
- Modified: `src/ZeroAlloc.Mediator.Authorization/{AuthorizationBehavior,AuthorizationOptions,MediatorAuthorizationServiceCollectionExtensions,ZeroAlloc.Mediator.Authorization}.csproj}.cs`
- Modified: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.*.txt`
- Deleted: `src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationGeneratedHooks.cs`
- Deleted: `src/ZeroAlloc.Mediator.Authorization.Generator/` (whole dir)
- Modified: 4 test files (`AuthorizationBehaviorTests`, `WithAuthorizationTests`, `IAuthorizedRequestTests`, `AllocationBudgetTests`)
- Deleted: `tests/ZeroAlloc.Mediator.Authorization.Tests/MediatorAuthorizationGeneratedHooksTests.cs`
- Modified: `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`
- Modified: `ZeroAlloc.Mediator.slnx`
- Modified: `apicompat-suppressions.xml`
- Modified: `release-please-config.json`
- Modified: `.release-please-manifest.json`
- New: `docs/plans/2026-05-19-mediator-authorization-v2-design.md` (already on branch from earlier)
- New: `docs/plans/2026-05-19-mediator-authorization-v2.md` (this plan, if you committed it)

**Step 5: Push.**

```powershell
git push -u origin feat/authorization-v2-split-versioning
```

**Step 6: Open PR.**

```powershell
$body = @'
## Summary
ZeroAlloc.Mediator.Authorization v2.0.0 — coupled follow-on to `ZeroAlloc.Authorization` v2.0.0 (shipped in https://github.com/ZeroAlloc-Net/ZeroAlloc.Authorization/pull/19). Deletes the in-package source generator + static delegate plumbing, rewrites `AuthorizationBehavior` to consume `AuthorizerFor<T>` via DI generic dispatch (matching `Mediator.Validation` pattern). Per-package versioning starts here: Authorization at v2.0.0, core stays at v4.x.

## Breaking changes
- `MediatorAuthorizationGeneratedHooks` static class removed
- `AuthorizationOptions.AutoRegisterDiscoveredPolicies` property + `ValidatePoliciesAreRegistered()` method removed
- `WithAuthorization()` adds a startup guard that throws if `services.AddZeroAllocAuthorization()` wasn't called first
- Hard floor: `ZeroAlloc.Authorization >= 2.0.0`

## Consumer migration
One new line in Program.cs:
```diff
+ services.AddZeroAllocAuthorization();
  services.AddMediator(b => b.WithAuthorization(auth => auth.UseAccessor<MyAccessor>()));
```
Plus the per-policy migration (`[AuthorizationPolicy]` → `[Policy]`, `[Authorize]` → `[RequirePolicy]`, single async `EvaluateAsync`) inherited from ZA.Authorization v2.

## Versioning
This release splits Mediator.Authorization from core's release line via release-please config update:
- `ZeroAlloc.Mediator` (core): stays on v4.x trajectory, no bump
- `ZeroAlloc.Mediator.Authorization`: NEW v2.0.0 (was previously lockstep at v4.1.x)

Future Authorization-only changes will no longer force a core major bump.

## Test plan
- [x] `dotnet test` green (all rewritten suites)
- [x] AOT smoke: new `[RequirePolicy]` scenarios assert 0B allocated on happy path
- [x] D3 guard tested (positive + negative + wrong-order)
- [x] `apicompat-suppressions.xml` covers all v2 breaks with `IsBaselineSuppression=true` + per-TFM Left/Right paths
- [x] `release-please-config.json` validates as JSON; manifest has both package versions

## Design reference
[docs/plans/2026-05-19-mediator-authorization-v2-design.md](docs/plans/2026-05-19-mediator-authorization-v2-design.md)
'@

gh pr create --repo ZeroAlloc-Net/ZeroAlloc.Mediator --title "feat(authorization)!: v2.0.0 — split versioning + consume AuthorizerFor<T> via DI" --body $body --base main
```

**Step 7: Watch CI.**

Same gates as PR #19:
- `build`, `aot-smoke`, `lint-commits`, `api-compat`

Expected potential failures and fixes:
- **lint-commits**: subjects must be lowercase, ≤ 100 chars. The PR title above is 89 chars and starts lowercase — should pass. If individual commits fail (the breaking-change commits have long bodies), check `git log --oneline` and ensure each subject is lowercase + under 100.
- **api-compat**: if entries in `apicompat-suppressions.xml` don't match exactly, the diagnostic fires. Re-verify Target paths, Left/Right TFM strings, `IsBaselineSuppression=true`.
- **aot-smoke**: if any reference to deleted types lingers in the sample, build will fail.

If anything fails, pull the failing-job log via `gh run view <runId> --log --job <jobId>` and iterate.

---

## Notes for the executor

- **Conventional-commit scope is mandatory.** Every commit subject must start with `<type>(authorization)<!>:` so release-please attributes the bump to the Authorization package, not core. Verify before each commit with `git log -1 --format=%s` and re-run `git commit --amend` if missing.
- **DRY:** the test setup pattern is the same across Tasks 5-8; if you find yourself repeating verbatim setup, extract a `private static IServiceProvider BuildSp(...)` helper into a single shared test file (e.g., `AuthorizationTestHarness.cs`). Don't over-abstract.
- **YAGNI:** the dead options in `AuthorizationOptions` (`AutoRegisterDiscoveredPolicies`, `ValidatePoliciesAreRegistered`) are gone. Don't reintroduce them as `[Obsolete]` for compat — v2 is a clean break.
- **TDD:** Tasks 5-8 follow the pattern "old test fails → rewrite test → new test passes". The old tests are the failing tests for the rewrites.
- **One commit per task.** Conventional-commit prefixes drive release-please's v2.0.0 bump (and only on the Authorization package, thanks to the `(authorization)` scope).
- **Subagent dispatch hint:** Tasks 5-8 are mechanically similar; consider one combined dispatch for all four test rewrites with a single agent. Tasks 14-15 are tiny and can also batch.
