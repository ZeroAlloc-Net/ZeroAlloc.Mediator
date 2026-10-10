# AOT-safe failure results Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Under Native AOT, `ValidationBehavior` and `AuthorizationBehavior` return a failed result instead of throwing, with no reflection and no trim suppression.

**Architecture:** ZeroAlloc.Results gains `IFailureFactory<TSelf, E>`, implemented explicitly by `Result<T, E>`, `Result<T>` and `UnitResult<E>`. Mediator's two failure factories replace their `GetMethod` lookup with a one-time `default(TResponse) is IFailureFactory<TResponse, TError>` check, and the IL2091 suppressions go.

**Tech Stack:** C# / .NET 8-10, xunit 2, Native AOT (`PublishAot`), Microsoft.CodeAnalysis.PublicApiAnalyzers, release-please.

**Spec:** `docs/plans/2026-10-10-aot-failure-factories-design.md` (this branch of ZeroAlloc.Mediator).

## Global Constraints

- Interface: `public interface IFailureFactory<TSelf, E> { TSelf CreateFailure(E error); }` in namespace `ZeroAlloc.Results`, invariant in both type parameters, `[EditorBrowsable(EditorBrowsableState.Never)]`.
- Implementations are explicit interface implementations delegating to the type's existing static `Failure`.
- No reflection, no `UnconditionalSuppressMessage`, no `#pragma` or `NoWarn` for IL warnings anywhere in the change.
- Mediator's `ZeroAlloc.Results` floor in both `ZeroAlloc.Mediator.Validation` and `ZeroAlloc.Mediator.Authorization` becomes `1.4.0`.
- Never pack ZeroAlloc.Results locally under a real version number; local packs use `9.9.9-local.<short-sha>`.
- Commit subjects start lowercase; commit body lines are 100 characters or fewer; no nested parentheses in commit bodies; no `claude.ai/code/session_*` URLs anywhere.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: `IFailureFactory` in ZeroAlloc.Results

Repo: `C:\Projects\Prive\ZeroAlloc\ZeroAlloc.Results`. Work in a worktree:
`git worktree add .worktrees/failure-factory -b feat/failure-factory origin/main` (check `.worktrees/` is git-ignored first, via `git check-ignore`).

**Files:**
- Create: `src/ZeroAlloc.Results/IFailureFactory.cs`
- Modify: `src/ZeroAlloc.Results/Result2.cs`, `src/ZeroAlloc.Results/Result1.cs`, `src/ZeroAlloc.Results/UnitResult.cs` (type declaration line plus one explicit member each)
- Modify: `src/ZeroAlloc.Results/PublicAPI.Unshipped.txt` (whatever RS0016 asks for)
- Create: `tests/ZeroAlloc.Results.Tests/FailureFactoryTests.cs`
- Modify: `samples/ZeroAlloc.Results.AotSmoke/Program.cs`

**Interfaces:**
- Produces: `ZeroAlloc.Results.IFailureFactory<TSelf, E>` with `TSelf CreateFailure(E error)`; `Result<T, E> : IFailureFactory<Result<T, E>, E>`; `Result<T> : IFailureFactory<Result<T>, string>`; `UnitResult<E> : IFailureFactory<UnitResult<E>, E>`.

- [ ] **Step 1: Write the failing tests** in `tests/ZeroAlloc.Results.Tests/FailureFactoryTests.cs`:

```csharp
using Xunit;
using ZeroAlloc.Results;

namespace ZeroAlloc.Results.Tests;

public class FailureFactoryTests
{
    // The same one-time generic check a consumer such as a pipeline behaviour performs.
    private static Func<TError, TResult>? FactoryFor<TResult, TError>() =>
        default(TResult) is IFailureFactory<TResult, TError> factory ? factory.CreateFailure : null;

    [Fact]
    public void ResultTE_CreatesFailureWithError()
    {
        var create = FactoryFor<Result<int, string>, string>();

        Assert.NotNull(create);
        var result = create("boom");
        Assert.True(result.IsFailure);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void ResultT_CreatesFailureWithError()
    {
        var create = FactoryFor<Result<int>, string>();

        Assert.NotNull(create);
        var result = create("boom");
        Assert.True(result.IsFailure);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void UnitResult_CreatesFailureWithError()
    {
        var create = FactoryFor<UnitResult<string>, string>();

        Assert.NotNull(create);
        var result = create("boom");
        Assert.True(result.IsFailure);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void MismatchedErrorType_HasNoFactory()
    {
        Assert.Null(FactoryFor<Result<int, string>, object>());
        Assert.Null(FactoryFor<UnitResult<string>, int>());
    }

    [Fact]
    public void ReferenceTypeResponse_HasNoFactory()
    {
        Assert.Null(FactoryFor<string, string>());
    }

    [Fact]
    public void CreateFailure_IsNotAPublicMemberOfTheStructs()
    {
        Assert.Null(typeof(Result<int, string>).GetMethod("CreateFailure"));
        Assert.Null(typeof(Result<int>).GetMethod("CreateFailure"));
        Assert.Null(typeof(UnitResult<string>).GetMethod("CreateFailure"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ZeroAlloc.Results.Tests --filter FullyQualifiedName~FailureFactoryTests`
Expected: build error, `IFailureFactory<,>` not found. If the test project uses xunit v3 with `Program.cs`, use `dotnet run --project tests/ZeroAlloc.Results.Tests -- -class ZeroAlloc.Results.Tests.FailureFactoryTests` instead; check `tests/ZeroAlloc.Results.Tests/ZeroAlloc.Results.Tests.csproj`.

- [ ] **Step 3: Implement.** `src/ZeroAlloc.Results/IFailureFactory.cs`:

```csharp
using System.ComponentModel;

namespace ZeroAlloc.Results;

/// <summary>
/// Builds a failed result of type <typeparamref name="TSelf"/> from an error of type <typeparamref name="E"/>.
/// Lets generic code, such as a pipeline behaviour whose response type is a type parameter, return a
/// failure without reflection, which keeps it trimming and Native AOT safe.
/// WARNING: Never use as a variable or return type — doing so boxes the struct onto the heap.
/// Check for it once per closed type and cache the delegate:
/// <c>default(TResult) is IFailureFactory&lt;TResult, TError&gt; f ? f.CreateFailure : null</c>.
/// </summary>
/// <remarks>
/// <see cref="CreateFailure"/> ignores the instance it is called on; the result depends only on
/// <paramref name="error"/>. Both type parameters are invariant, so the error type must match exactly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IFailureFactory<TSelf, E>
{
    /// <summary>Creates a failed <typeparamref name="TSelf"/> containing <paramref name="error"/>.</summary>
    TSelf CreateFailure(E error);
}
```

In `Result2.cs`, change the declaration to
`public readonly struct Result<T, E> : IResult<T, E>, IFailureFactory<Result<T, E>, E>` and add after `Failure`:

```csharp
    /// <inheritdoc/>
    Result<T, E> IFailureFactory<Result<T, E>, E>.CreateFailure(E error) => Failure(error);
```

In `Result1.cs`: `public readonly struct Result<T> : IResult<T, string>, IFailureFactory<Result<T>, string>` and

```csharp
    /// <inheritdoc/>
    Result<T> IFailureFactory<Result<T>, string>.CreateFailure(string error) => Failure(error);
```

In `UnitResult.cs`: `public readonly struct UnitResult<E> : IFailureFactory<UnitResult<E>, E>` and

```csharp
    /// <inheritdoc/>
    UnitResult<E> IFailureFactory<UnitResult<E>, E>.CreateFailure(E error) => Failure(error);
```

Build, then add exactly the lines RS0016 reports to `PublicAPI.Unshipped.txt` (at least
`ZeroAlloc.Results.IFailureFactory<TSelf, E>` and `ZeroAlloc.Results.IFailureFactory<TSelf, E>.CreateFailure(E error) -> TSelf`), keeping the file sorted as it is.

- [ ] **Step 4: Run tests.** Run the full test project. Expected: all pass, 0 warnings.

- [ ] **Step 5: AOT smoke.** In `samples/ZeroAlloc.Results.AotSmoke/Program.cs`, add a numbered section after the `UnitResult<E>` one that goes through a generic local function, so ILC sees the interface check over a type parameter:

```csharp
// 4b. IFailureFactory — the generic, reflection-free failure construction pipeline behaviours use
if (!FailureVia<Result<int, string>, string>("f1", out var f1) || !string.Equals(f1.Error, "f1", StringComparison.Ordinal))
    return Fail("IFailureFactory on Result<int,string> did not build a failure");
if (!FailureVia<Result<int>, string>("f2", out var f2) || !string.Equals(f2.Error, "f2", StringComparison.Ordinal))
    return Fail("IFailureFactory on Result<int> did not build a failure");
if (!FailureVia<UnitResult<string>, string>("f3", out var f3) || !string.Equals(f3.Error, "f3", StringComparison.Ordinal))
    return Fail("IFailureFactory on UnitResult<string> did not build a failure");
```

and next to the existing `Fail` local function:

```csharp
static bool FailureVia<TResult, TError>(TError error, out TResult result)
{
    if (default(TResult) is IFailureFactory<TResult, TError> factory)
    {
        result = factory.CreateFailure(error);
        return true;
    }
    result = default!;
    return false;
}
```

Match the file's existing numbering and style. Then publish and run it the way CI does (read `.github/workflows/*.yml` for the AOT step). Locally Native AOT needs a `vcvars64` environment; if the publish cannot run locally, say so in the report and rely on CI. Expected: no IL/AOT warnings, the exe prints its pass line.

- [ ] **Step 6: Commit**

```
feat: add IFailureFactory for reflection-free failure construction

Result<T, E>, Result<T> and UnitResult<E> implement it explicitly. Generic code whose result type
is a type parameter can build a failure without reflection, so it stays trimming and AOT safe.
```

---

### Task 2: Mediator behaviours use `IFailureFactory`

Repo: the Mediator worktree `C:\Projects\Prive\ZeroAlloc\ZeroAlloc.Mediator\.worktrees\aot-failure-factories`, branch `fix/aot-failure-factories`.

Until Results 1.4.0 is on NuGet, verify against a local pack. From the Task 1 worktree:
`dotnet pack src/ZeroAlloc.Results -c Release -p:Version=9.9.9-local.<task-1-short-sha> -o <scratchpad>/feed`.
In Mediator, set both Results references to `9.9.9-local.<task-1-short-sha>` and pass
`-p:RestoreAdditionalProjectSources=<scratchpad>/feed` to every `dotnet build` / `dotnet test`.
Commit with the local version; Task 4 swaps it to `1.4.0`.

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Validation/FailureFactory.cs` (whole file)
- Modify: `src/ZeroAlloc.Mediator.Authorization/AuthorizationFailureFactory.cs` (whole file)
- Modify: `src/ZeroAlloc.Mediator.Validation/ValidationBehavior.cs:14-23` (delete the IL2091 comment block and attribute)
- Modify: `src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs:37-44` (same), and its `<remarks>` deny-semantics list
- Modify: both csproj files' `ZeroAlloc.Results` reference
- Modify: `docs/authorization.md` Result path section
- Test: `tests/ZeroAlloc.Mediator.Validation.Tests/ValidationBehaviorTests.cs`, `tests/ZeroAlloc.Mediator.Authorization.Tests/AuthorizationBehaviorTests.cs`, `tests/ZeroAlloc.Mediator.Authorization.Tests/TestFixtures.cs`

**Interfaces:**
- Consumes: `ZeroAlloc.Results.IFailureFactory<TSelf, E>` from Task 1.
- Produces: unchanged internal API — `FailureFactory<TResponse>.Create` (`Func<ValidationError, TResponse>?`) and `AuthorizationFailureFactory<TResponse>.Create` (`Func<AuthorizationFailure, TResponse>?`).

- [ ] **Step 1: Write the failing tests.**

In `ValidationBehaviorTests.cs`, add a request type, stub handler and validator next to the existing ones:

```csharp
public readonly record struct UnitValidatedRequest(string Name) : IRequest<UnitResult<ValidationError>>;

public sealed class UnitValidatedRequestHandler : IRequestHandler<UnitValidatedRequest, UnitResult<ValidationError>>
{
    public ValueTask<UnitResult<ValidationError>> Handle(UnitValidatedRequest request, CancellationToken ct) =>
        ValueTask.FromResult(UnitResult<ValidationError>.Success());
}

public sealed class UnitValidatedRequestValidator : ValidatorFor<UnitValidatedRequest>
{
    public override ValidationResult Validate(UnitValidatedRequest instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Name))
            return new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "must not be empty" }]);

        return new ValidationResult([]);
    }
}
```

and tests in the class:

```csharp
    [Fact]
    public async Task ValidationFails_UnitResultResponse_ReturnsFailureResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<UnitValidatedRequest>, UnitValidatedRequestValidator>();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        var nextCalled = false;
        ValueTask<UnitResult<ValidationError>> Next(UnitValidatedRequest r, CancellationToken c)
        {
            nextCalled = true;
            return ValueTask.FromResult(UnitResult<ValidationError>.Success());
        }

        var result = await ValidationBehavior.Handle(
            new UnitValidatedRequest(""), CancellationToken.None, Next);

        Assert.False(nextCalled);
        Assert.True(result.IsFailure);
        Assert.Equal("Name", Assert.Single(result.Error.Failures).PropertyName);
    }

    [Fact]
    public async Task ValidationFails_PlainResponse_ThrowsValidationFailedException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<ThrowingRequest>, ThrowingRequestValidator>();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        ValueTask<string> Next(ThrowingRequest r, CancellationToken c) => ValueTask.FromResult(r.Name);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
            await ValidationBehavior.Handle(new ThrowingRequest(""), CancellationToken.None, Next));

        Assert.Equal("Name", Assert.Single(ex.Error.Failures).PropertyName);
    }
```

If a test equivalent to the second one already exists, keep the existing one and skip adding it. Check the exception's error property name in `ValidationFailedException.cs` and use it.

In `TestFixtures.cs`, add next to `GetThingResultDeny`, using the same `[RequirePolicy(...)]` attribute that `GetThingResultDeny` carries:

```csharp
[RequirePolicy(/* same policy name as GetThingResultDeny */)]
public sealed record GetThingUnitDeny(int Id) : IRequest<UnitResult<AuthorizationFailure>>;
```

and next to the other stub handlers:

```csharp
public sealed class StubGetThingUnitDenyHandler : IRequestHandler<GetThingUnitDeny, UnitResult<AuthorizationFailure>>
{ public ValueTask<UnitResult<AuthorizationFailure>> Handle(GetThingUnitDeny r, CancellationToken ct) => new(UnitResult<AuthorizationFailure>.Success()); }
```

In `AuthorizationBehaviorTests.cs`, after `Deny_OnIAuthorizedRequest_ReturnsFailureResult`:

```csharp
    [Fact]
    public async Task Deny_OnUnitResultResponse_ReturnsFailureResult()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        var nextCalled = false;
        ValueTask<UnitResult<AuthorizationFailure>> Next(GetThingUnitDeny r, CancellationToken c)
        { nextCalled = true; return new(UnitResult<AuthorizationFailure>.Success()); }

        var result = await Invoke<GetThingUnitDeny, UnitResult<AuthorizationFailure>>(
            scope, new GetThingUnitDeny(5), Next);

        Assert.False(nextCalled);
        Assert.True(result.IsFailure);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
    }
```

- [ ] **Step 2: Run to verify the UnitResult tests fail.** Run both test projects with the local feed (still on Results 1.3.0, before switching the reference). Expected: the two UnitResult tests fail with `ValidationFailedException` / `AuthorizationDeniedException`; everything else passes.

- [ ] **Step 3: Implement.** Switch both Results references to `9.9.9-local.<task-1-short-sha>`. Replace `src/ZeroAlloc.Mediator.Validation/FailureFactory.cs` with:

```csharp
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Validation;

// Per-TResponse static cache of the failure constructor. Result<T, ValidationError> and
// UnitResult<ValidationError> implement IFailureFactory, so this needs no reflection and stays
// trimming and Native AOT safe. The value-type default boxes once, here, never per request.
// Any other TResponse leaves Create null, and the behaviour throws ValidationFailedException.
internal static class FailureFactory<TResponse>
{
    internal static readonly Func<ValidationError, TResponse>? Create =
        default(TResponse) is IFailureFactory<TResponse, ValidationError> factory
            ? factory.CreateFailure
            : null;
}
```

Replace `src/ZeroAlloc.Mediator.Authorization/AuthorizationFailureFactory.cs` with:

```csharp
using System;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

// Per-TResponse static cache of the failure constructor. Result<T, AuthorizationFailure> and
// UnitResult<AuthorizationFailure> implement IFailureFactory, so this needs no reflection and stays
// trimming and Native AOT safe. The value-type default boxes once, here, never per request.
// Any other TResponse leaves Create null, and the behaviour throws AuthorizationDeniedException.
internal static class AuthorizationFailureFactory<TResponse>
{
    internal static readonly Func<AuthorizationFailure, TResponse>? Create =
        default(TResponse) is IFailureFactory<TResponse, AuthorizationFailure> factory
            ? factory.CreateFailure
            : null;
}
```

In both behaviours, delete the `// IL2091: ...` comment block and the `[UnconditionalSuppressMessage(...)]` attribute above `Handle`, and remove `using System.Diagnostics.CodeAnalysis;` if nothing else in the file needs it. In `AuthorizationBehavior`'s `<remarks>`, change the second deny-semantics bullet to cover both shapes:

```xml
///   <item><c>Result&lt;T, AuthorizationFailure&gt;</c>-shaped response (incl.
///         <see cref="IAuthorizedRequest{TResponse}"/>) or <c>UnitResult&lt;AuthorizationFailure&gt;</c> ⇒
///         returns a failed result carrying the <see cref="AuthorizationFailure"/>.</item>
```

In `docs/authorization.md`, after the paragraph "The handler still returns plain `T` — the wrap is symmetric, hidden in the behavior.", add:

```markdown
A request that declares `IRequest<UnitResult<AuthorizationFailure>>` gets the same treatment: a denied call returns a failed `UnitResult` instead of throwing. Both shapes build the failure without reflection, so the behaviour is identical under Native AOT.
```

- [ ] **Step 4: Run tests.** Full `ZeroAlloc.Mediator.Validation.Tests` and `ZeroAlloc.Mediator.Authorization.Tests`, plus a Release build of the whole solution with the local feed. Expected: all pass, 0 warnings, no RS0016/RS0017 (the factories are internal).

- [ ] **Step 5: Commit**

```
fix: build validation and authorization failures without reflection

FailureFactory and AuthorizationFailureFactory looked up Result.Failure with GetMethod, behind an
IL2091 suppression. Under Native AOT the method was trimmed, so both behaviours threw instead of
returning a failed result. They now use IFailureFactory from ZeroAlloc.Results, and the
suppressions are gone. UnitResult responses now also get a failed result.

Fixes #283
```

---

### Task 3: AOT smoke covers failure results

Same Mediator worktree and local feed as Task 2.

**Files:**
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs`
- Create: `samples/ZeroAlloc.Mediator.AotSmoke/Validation/ValidatedScenario.cs`
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/Program.cs`
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj`

**Interfaces:**
- Consumes: Task 2's behaviours; `ValidatorFor<T>`, `ValidationResult`, `ValidationFailure` from ZeroAlloc.Validation.

- [ ] **Step 1: Authorization scenario.** In `AuthorizedScenario.cs`:
  - Delete the `[DynamicDependency(...)]` attribute and the comment block above it. Remove `using System.Diagnostics.CodeAnalysis;` if unused.
  - Add request types with handlers, all carrying `[RequirePolicy("AotAdmin")]`:

```csharp
public sealed record AotPayload(int Id);
public readonly record struct AotValuePayload(int Id);

[RequirePolicy("AotAdmin")]
public sealed record AotClassDeny(int Id) : IRequest<Result<AotPayload, AuthorizationFailure>>;

[RequirePolicy("AotAdmin")]
public sealed record AotStructDeny(int Id) : IRequest<Result<AotValuePayload, AuthorizationFailure>>;

[RequirePolicy("AotAdmin")]
public sealed record AotUnitDeny(int Id) : IRequest<UnitResult<AuthorizationFailure>>;
```

    with stub handlers in the existing style, for example:

```csharp
public sealed class AotClassDenyHandler : IRequestHandler<AotClassDeny, Result<AotPayload, AuthorizationFailure>>
{
    public ValueTask<Result<AotPayload, AuthorizationFailure>> Handle(AotClassDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotPayload, AuthorizationFailure>>(new AotPayload(r.Id));
}
public sealed class AotStructDenyHandler : IRequestHandler<AotStructDeny, Result<AotValuePayload, AuthorizationFailure>>
{
    public ValueTask<Result<AotValuePayload, AuthorizationFailure>> Handle(AotStructDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotValuePayload, AuthorizationFailure>>(new AotValuePayload(r.Id));
}
public sealed class AotUnitDenyHandler : IRequestHandler<AotUnitDeny, UnitResult<AuthorizationFailure>>
{
    public ValueTask<UnitResult<AuthorizationFailure>> Handle(AotUnitDeny r, CancellationToken ct)
        => ValueTask.FromResult(UnitResult<AuthorizationFailure>.Success());
}
```

  - In `VerifyResultPath`, after the existing deny block, add one deny block per new type with `anonCtx`, in the same `using (var sp ...)` / accessor-resolve shape, calling `AuthorizationBehavior.Handle<AotClassDeny, Result<AotPayload, AuthorizationFailure>>` and so on, and throwing `InvalidOperationException("<type>-deny did not return Failure")` when `IsSuccess` is true or `Error.Code` is not `AuthorizationFailure.DefaultDenyCode`. Extract a private generic helper if the four blocks would otherwise repeat verbatim.

- [ ] **Step 2: Validation scenario.** Add `<ProjectReference Include="..\..\src\ZeroAlloc.Mediator.Validation\ZeroAlloc.Mediator.Validation.csproj" SetTargetFramework="TargetFramework=net10.0" />` to the smoke csproj. Create `Validation/ValidatedScenario.cs` in namespace `ZeroAlloc.Mediator.AotSmoke.Validation` with:
  - requests `AotValidateInt(string Name) : IRequest<Result<int, ValidationError>>`, `AotValidateClass(string Name) : IRequest<Result<AotValidatedPayload, ValidationError>>` (with `public sealed record AotValidatedPayload(string Name)`), `AotValidateStruct(string Name) : IRequest<Result<AotValidatedValue, ValidationError>>` (with `public readonly record struct AotValidatedValue(string Name)`), `AotValidateUnit(string Name) : IRequest<UnitResult<ValidationError>>`;
  - a stub handler per request (required by ZAM001);
  - a hand-written `ValidatorFor<T>` per request that fails when `Name` is empty, in the shape of `ValidatedRequestValidator` in `tests/ZeroAlloc.Mediator.Validation.Tests/ValidationBehaviorTests.cs`;
  - `public static void Run()` that builds `new ServiceCollection()`, registers the four validators as singletons `ValidatorFor<T>`, calls `services.AddMediator().WithValidation()`, builds the provider, resolves `IMediator` once to wire `ValidationBehaviorState`, then for each request calls `ValidationBehavior.Handle<TRequest, TResponse>(new TRequest(""), CancellationToken.None, next)` with a `next` that throws `InvalidOperationException("next must not run")`, and throws `InvalidOperationException` unless the result `IsFailure` and its `Error.Failures` holds exactly one failure for `Name`. Print `Mediator.Validation: failure results OK`.
  - Use a generic private helper so the four checks don't repeat.

  In `Program.cs`, call `ZeroAlloc.Mediator.AotSmoke.Validation.ValidatedScenario.Run();` before the authorization scenario, and add "Validation" to the header comment's bridge list.

- [ ] **Step 3: Publish and run.** Publish the smoke project the way CI does (find the step in `.github/workflows/`), with the local feed, in a `vcvars64` environment, and run the exe. Expected: no IL or AOT warning (the csproj promotes IL2026/IL2067/IL2075/IL2091/IL3050/IL3051 to errors), exit code 0, `AOT smoke: PASS`.

  Prove the smoke catches the bug: temporarily switch Mediator's Validation and Authorization Results references back to `1.3.0` and restore `FailureFactory.cs` / `AuthorizationFailureFactory.cs` and the two behaviours from `origin/main` (`git checkout origin/main -- <paths>`), publish and run: expected a non-zero exit from a failure-result check. Then restore the branch versions (`git checkout HEAD -- <paths>`) and re-run to PASS. Record both outputs in the report.

- [ ] **Step 4: Commit**

```
test: cover validation and authorization failure results in the AOT smoke

Adds class, struct and UnitResult failure cases for both behaviours and drops the DynamicDependency
that hid the authorization bug, so CI's AOT publish catches a regression.
```

---

### Task 4: Ship (controller, not a subagent)

1. Push the Results branch, open its PR (`feat:` title), merge when every check is green.
2. Ask the maintainer for a one-off Results 1.4.0 release; wait until `ZeroAlloc.Results` 1.4.0 is on NuGet.
3. In the Mediator worktree, swap both Results references from `9.9.9-local.*` to `1.4.0`, build and test without the local feed, commit it as `build: depend on ZeroAlloc.Results 1.4.0`,
   push, and open the PR. The PR body carries a "Behaviour change" section for the `UnitResult` responses, and a `BEGIN_COMMIT_OVERRIDE` with:

```
fix: build validation and authorization failures without reflection
feat: return a failed UnitResult from the validation and authorization behaviours
```

4. Merge when every check is green; confirm both lines appear in Mediator's release-please PR.
