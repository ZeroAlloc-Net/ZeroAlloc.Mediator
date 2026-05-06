# ZeroAlloc.Mediator.Authorization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Ship `ZeroAlloc.Mediator.Authorization` as a 6th sub-package in this repo (alongside `Mediator.Cache`/`Resilience`/`Telemetry`/`Validation`). Adapts `ZeroAlloc.Authorization` for request-handler authz via a single hand-coded `AuthorizationBehavior<TRequest, TResponse>` plus a Roslyn generator that emits a per-compilation `GeneratedAuthorizationLookup` static class.

**Architecture:** Two layers. Hand-coded runtime behavior (~120 LOC, mirrors `Mediator.Validation`'s file layout). Roslyn `IIncrementalGenerator` in a new generator sub-project that emits one lookup file per compilation. Dual-path failure semantics — throw for `IRequest<T>`, `Result<T, AuthorizationFailure>` for `IAuthorizedRequest<T>`.

**Tech Stack:** .NET 8/10 multi-target (matches Mediator.Validation), xUnit 2.x, Verify (snapshot tests), Roslyn 4.14, Native AOT publish.

**Design doc:** [`2026-05-06-mediator-authorization-design.md`](2026-05-06-mediator-authorization-design.md)

---

## Working branch

`feat/mediator-authorization` (already created from `main`). All tasks land on this branch. Final task pushes and opens a PR.

All tasks run from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/` unless noted.

The repo uses single-package release-please (manifest at `4.0.0` covering everything). New sub-package gets versioned in lockstep with the family.

---

## Task 1: Sub-package csproj + slnx wiring

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj`
- Create: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Shipped.txt`
- Create: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt`
- Modify: `ZeroAlloc.Mediator.slnx`

**Step 1.1: Copy the Validation csproj as a template**

```bash
cp src/ZeroAlloc.Mediator.Validation/ZeroAlloc.Mediator.Validation.csproj \
   src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj
```

Then edit the new file to:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <PackageId>ZeroAlloc.Mediator.Authorization</PackageId>
    <Description>Pipeline behavior that authorizes IRequest&lt;T&gt; (or IAuthorizedRequest&lt;T&gt;) via ZeroAlloc.Authorization before dispatch. Source-generated per-request policy lookup; deny throws AuthorizationDeniedException or returns Result&lt;T, AuthorizationFailure&gt; depending on the request marker.</Description>
    <Version>0.0.0-local</Version>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\ZeroAlloc.Mediator\ZeroAlloc.Mediator.csproj" />
    <PackageReference Include="ZeroAlloc.Authorization" Version="1.1.*" />
    <PackageReference Include="ZeroAlloc.Results" Version="1.1.*" />
    <!-- Re-enable after Task 6 creates the generator project:
    <ProjectReference Include="..\ZeroAlloc.Mediator.Authorization.Generator\ZeroAlloc.Mediator.Authorization.Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" /> -->
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Tests" />
    <InternalsVisibleTo Include="ZeroAlloc.Mediator.Authorization.Generator.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.3" />
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
  </ItemGroup>
</Project>
```

**Step 1.2: Create empty PublicAPI files**

```bash
echo "#nullable enable" > src/ZeroAlloc.Mediator.Authorization/PublicAPI.Shipped.txt
echo "#nullable enable" > src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt
```

**Step 1.3: Add the new project to `ZeroAlloc.Mediator.slnx`**

In the `<Folder Name="/src/">` block, add (alphabetical with siblings):

```xml
<Project Path="src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj" />
```

**Step 1.4: Verify build**

```
dotnet build ZeroAlloc.Mediator.slnx -c Release
```

Expected: 0 errors. The new project has no source files yet; it builds an empty assembly.

**Step 1.5: Commit**

```
git add src/ZeroAlloc.Mediator.Authorization/ ZeroAlloc.Mediator.slnx
git commit -m "chore(mediator.authorization): scaffold sub-package csproj"
```

---

## Task 2: Test project scaffold

**Files:**
- Create: `tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj`
- Modify: `ZeroAlloc.Mediator.slnx`

**Step 2.1: Copy the Validation tests csproj as a template**

```bash
cp tests/ZeroAlloc.Mediator.Validation.Tests/ZeroAlloc.Mediator.Validation.Tests.csproj \
   tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj
```

Edit:
- `<RootNamespace>ZeroAlloc.Mediator.Authorization.Tests</RootNamespace>`
- `<ProjectReference Include="..\..\src\ZeroAlloc.Mediator.Authorization\ZeroAlloc.Mediator.Authorization.csproj" />`

**Step 2.2: Add to slnx** in `<Folder Name="/tests/">`.

**Step 2.3: Build + commit**

```
dotnet build ZeroAlloc.Mediator.slnx -c Release   # expect 0 errors
git add tests/ZeroAlloc.Mediator.Authorization.Tests/ ZeroAlloc.Mediator.slnx
git commit -m "chore(mediator.authorization): scaffold test project"
```

---

## Task 3: Runtime — IAuthorizedRequest marker interface

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization/IAuthorizedRequest.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Tests/IAuthorizedRequestTests.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt`

**Step 3.1: Write failing test**

```csharp
// tests/ZeroAlloc.Mediator.Authorization.Tests/IAuthorizedRequestTests.cs
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

public class IAuthorizedRequestTests
{
    [Fact]
    public void IAuthorizedRequest_Extends_IRequest_Of_ResultWrappedResponse()
    {
        var iface = typeof(IAuthorizedRequest<int>);
        Assert.True(iface.IsInterface);

        var implementsRequest = iface.GetInterfaces()
            .Any(t => t.IsGenericType
                   && t.GetGenericTypeDefinition() == typeof(IRequest<>)
                   && t.GenericTypeArguments[0].IsGenericType
                   && t.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(Result<,>));
        Assert.True(implementsRequest, "IAuthorizedRequest<T> must extend IRequest<Result<T, AuthorizationFailure>>");
    }
}
```

**Step 3.2: Run, expect compile FAIL**

```
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests/ -c Release
```

Expected: `CS0246: Could not find 'IAuthorizedRequest<>'`.

**Step 3.3: Implement**

```csharp
// src/ZeroAlloc.Mediator.Authorization/IAuthorizedRequest.cs
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>Marker interface — opt into the Result-shaped deny path for an authorized request.</summary>
/// <remarks>
/// Plain <see cref="IRequest{TResponse}"/> + <see cref="Authorization.AuthorizeAttribute"/>
/// causes the behavior to throw <see cref="AuthorizationDeniedException"/> on deny.
/// Replacing <c>IRequest&lt;T&gt;</c> with <c>IAuthorizedRequest&lt;T&gt;</c> changes the
/// emission shape: the behavior returns
/// <c>Result&lt;T, <see cref="AuthorizationFailure"/>&gt;.Failure(...)</c> on deny instead.
/// </remarks>
public interface IAuthorizedRequest<TResponse>
    : IRequest<Result<TResponse, AuthorizationFailure>>
{
}
```

Append to `PublicAPI.Unshipped.txt`:

```
ZeroAlloc.Mediator.Authorization.IAuthorizedRequest<TResponse>
```

**Step 3.4: Run, expect PASS** (1 test).

**Step 3.5: Commit**

```
git add src/ZeroAlloc.Mediator.Authorization/IAuthorizedRequest.cs \
        src/ZeroAlloc.Mediator.Authorization/PublicAPI.Unshipped.txt \
        tests/ZeroAlloc.Mediator.Authorization.Tests/IAuthorizedRequestTests.cs
git commit -m "feat(mediator.authorization): add IAuthorizedRequest marker interface"
```

---

## Task 4: Runtime — AuthorizationDeniedException

Same TDD shape. Test first, fail, implement, pass, commit.

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization/AuthorizationDeniedException.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Tests/AuthorizationDeniedExceptionTests.cs`

```csharp
// Test
public class AuthorizationDeniedExceptionTests
{
    [Fact]
    public void Exception_CarriesFailureWithCode()
    {
        var failure = new AuthorizationFailure("policy.deny.role.admin");
        var ex = new AuthorizationDeniedException(failure);
        Assert.Equal(failure, ex.Failure);
        Assert.Contains("policy.deny.role.admin", ex.Message);
    }
}
```

```csharp
// Impl
public sealed class AuthorizationDeniedException : Exception
{
    public AuthorizationFailure Failure { get; }
    public AuthorizationDeniedException(AuthorizationFailure failure)
        : base($"Authorization denied: {failure.Code}") => Failure = failure;
}
```

Append three lines to `PublicAPI.Unshipped.txt` (type + ctor + Failure property).

```
git commit -m "feat(mediator.authorization): add AuthorizationDeniedException"
```

---

## Task 5: Runtime — ISecurityContextAccessor + AuthorizationOptions + WithAuthorization() builder

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization/ISecurityContextAccessor.cs`
- Create: `src/ZeroAlloc.Mediator.Authorization/AuthorizationOptions.cs`
- Create: `src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationServiceCollectionExtensions.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Tests/WithAuthorizationTests.cs`

**Test surface:** four facts — `UseAnonymousSecurityContext` registers singleton; `UseSecurityContextFactory` resolves from factory; `UseAccessor<>` resolves via accessor; missing `Use*` throws at app build.

(Implementation follows the design doc — `AuthorizationOptions` records which `Use*` was called, `WithAuthorization()` validates eagerly.)

```
git commit -m "feat(mediator.authorization): WithAuthorization builder + options + accessor"
```

---

## Task 6: Generator project scaffolding

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/ZeroAlloc.Mediator.Authorization.Generator.csproj`
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/AuthorizationGenerator.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj` (re-enable the analyzer ProjectReference commented in Task 1)
- Modify: `ZeroAlloc.Mediator.slnx`

**Step 6.1: Generator csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>ZeroAlloc.Mediator.Authorization.Generator</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="4.14.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 6.2: Empty generator class**

```csharp
namespace ZeroAlloc.Mediator.Authorization.Generator;

[Microsoft.CodeAnalysis.Generator(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class AuthorizationGenerator : Microsoft.CodeAnalysis.IIncrementalGenerator
{
    public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext context)
    {
        // Emission added in Tasks 7-8.
    }
}
```

**Step 6.3: Re-enable the analyzer ProjectReference in the main lib csproj**, add the new generator project to `ZeroAlloc.Mediator.slnx`.

**Step 6.4: Build + commit**

```
dotnet build ZeroAlloc.Mediator.slnx -c Release
git add src/ZeroAlloc.Mediator.Authorization.Generator/ \
        src/ZeroAlloc.Mediator.Authorization/ZeroAlloc.Mediator.Authorization.csproj \
        ZeroAlloc.Mediator.slnx
git commit -m "chore(mediator.authorization): scaffold generator project"
```

---

## Task 7: Generator — discover + emit GeneratedAuthorizationLookup (single policy, single request)

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/PolicyDiscovery.cs`
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/RequestDiscovery.cs`
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/LookupEmitter.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization.Generator/AuthorizationGenerator.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Generator.Tests/ZeroAlloc.Mediator.Authorization.Generator.Tests.csproj`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Generator.Tests/TestHarness.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Generator.Tests/LookupEmissionTests.cs`
- Modify: `ZeroAlloc.Mediator.slnx`

**Step 7.1: Generator-tests csproj**

Mirror `tests/ZeroAlloc.Mediator.Validation.Tests` structure but with these added packages:

```xml
<PackageReference Include="Verify.Xunit" Version="28.*" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
```

ProjectReference: only the generator project (not the runtime).

**Step 7.2: TestHarness.cs**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZeroAlloc.Mediator.Authorization.Generator;

namespace ZeroAlloc.Mediator.Authorization.Generator.Tests;

internal static class TestHarness
{
    public static string RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new AuthorizationGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        return string.Join("\n// ===== next file =====\n",
            result.Results
                .SelectMany(r => r.GeneratedSources)
                .Select(s => $"// {s.HintName}\n{s.SourceText}"));
    }

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
        => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
}
```

**Step 7.3: First snapshot test (single policy + single request, throw path)**

```csharp
[Fact]
public Task Emits_Lookup_For_SinglePolicy_OnPlainIRequest()
{
    var source = """
        using ZeroAlloc.Authorization;
        using ZeroAlloc.Mediator;

        [AuthorizationPolicy("AdminOnly")]
        public sealed class AdminOnlyPolicy : IAuthorizationPolicy
        {
            public bool IsAuthorized(ISecurityContext ctx) => false;
        }

        [Authorize("AdminOnly")]
        public sealed record GetOrderById(int Id) : IRequest<int>;
        """;
    return Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
}
```

**Step 7.4: Run, expect FAIL** (no snapshot baseline, generator emits nothing yet).

**Step 7.5: Implement PolicyDiscovery + RequestDiscovery + LookupEmitter**

`PolicyDiscovery.cs` — given a `Compilation`, walk all named types looking for the `[AuthorizationPolicy("...")]` attribute. Return `List<PolicyModel>` with `Name`, `FullyQualifiedTypeName`, `Location`.

`RequestDiscovery.cs` — walk all named types implementing `IRequest<>` or `IAuthorizedRequest<>`, return `List<RequestModel>` with `FullyQualifiedTypeName`, `ResponseTypeFqn`, `IsResultPath` (true if implements `IAuthorizedRequest<>`), `PolicyNames` (list, in declaration order).

`LookupEmitter.cs` — given the two lists, emit:

```csharp
internal static class GeneratedAuthorizationLookup
{
    private static readonly string[] EmptyPolicies = System.Array.Empty<string>();

    public static string[] GetPoliciesFor<TRequest>()
    {
        if (typeof(TRequest) == typeof(global::GetOrderById)) return new[] { "AdminOnly" };
        return EmptyPolicies;
    }

    public static IAuthorizationPolicy Resolve(string name, IServiceProvider sp) => name switch
    {
        "AdminOnly" => sp.GetRequiredService<global::AdminOnlyPolicy>(),
        _ => throw new InvalidOperationException($"Unknown policy '{name}'"),
    };

    public static System.Type[] PolicyTypes { get; } = new[]
    {
        typeof(global::AdminOnlyPolicy),
    };
}
```

Wire into `AuthorizationGenerator.Initialize()`:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var compProvider = context.CompilationProvider;
    context.RegisterSourceOutput(compProvider, (spc, comp) =>
    {
        var policies = PolicyDiscovery.Discover(comp).ToList();
        var requests = RequestDiscovery.Discover(comp, policies).ToList();
        if (policies.Count == 0 && requests.Count == 0) return;
        spc.AddSource("GeneratedAuthorizationLookup.g.cs", LookupEmitter.Emit(policies, requests));
    });
}
```

**Step 7.6: Run test → snapshot fails on first run** (Verify creates `.received.txt`). Inspect the received output. If correct, rename `.received.txt` → `.verified.txt`. Re-run; expect PASS.

**Step 7.7: Commit**

```
git add src/ZeroAlloc.Mediator.Authorization.Generator/ tests/ZeroAlloc.Mediator.Authorization.Generator.Tests/ ZeroAlloc.Mediator.slnx
git commit -m "feat(mediator.authorization): emit GeneratedAuthorizationLookup from compilation scan"
```

---

## Task 8: Generator — stacked policies + Result path

Add three more snapshot tests:

- `Emits_Lookup_For_Stacked_Policies` — two `[Authorize]` on one request; `GetPoliciesFor<T>()` returns both names in declaration order.
- `Emits_Lookup_For_IAuthorizedRequest` — request implements `IAuthorizedRequest<T>` instead of `IRequest<T>`. Generator emits the same lookup (the lookup itself is identical for both shapes — runtime behavior decides what to do based on `typeof(TResponse)`).
- `Emits_Nothing_For_Compilation_WithoutAnyAuthorize` — no `[Authorize]` anywhere → no `.g.cs`.

For each: write test → run → fail → adjust `RequestDiscovery` / `LookupEmitter` to handle the case → run → pass → snapshot review → rename `.received` → `.verified` → re-run → pass → commit.

```
git commit -m "feat(mediator.authorization): generator handles stacked policies + Result path detection"
```

---

## Task 9: Generator — emit GeneratedAuthorizationDIExtensions for auto-registration

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Authorization.Generator/LookupEmitter.cs`
- Add snapshot test: `Emits_DIExtensions_For_DiscoveredPolicies`

The generator emits a second file:

```csharp
// GeneratedAuthorizationDIExtensions.g.cs
internal static class GeneratedAuthorizationDIExtensions
{
    public static void RegisterDiscoveredPolicies(IServiceCollection services)
    {
        services.AddScoped<global::AdminOnlyPolicy>();
        services.AddScoped<global::OtherPolicy>();
    }
}
```

This lets `WithAuthorization()` auto-register policies (Task 11 wires this).

```
git commit -m "feat(mediator.authorization): emit DI auto-registration helper for policies"
```

---

## Task 10: Generator — diagnostics ZAMA001-ZAMA006

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization.Generator/Diagnostics.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization.Generator/AuthorizationGenerator.cs`
- Create: `tests/ZeroAlloc.Mediator.Authorization.Generator.Tests/DiagnosticTests.cs`

Define six `DiagnosticDescriptor`s. For each, write one test that compiles a source string triggering the diagnostic and asserts the generator reports it. Implement detection inside the generator. Run, pass, commit.

```
git commit -m "feat(mediator.authorization): diagnostics ZAMA001-ZAMA006"
```

---

## Task 11: Runtime — AuthorizationBehavior + WithAuthorization eager validation + auto-registration

**Files:**
- Create: `src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization/MediatorAuthorizationServiceCollectionExtensions.cs`
- Modify: `src/ZeroAlloc.Mediator.Authorization/AuthorizationOptions.cs` — add `AutoRegisterDiscoveredPolicies` flag (default true).
- Add tests: `AuthorizationBehaviorTests.cs` (the bulk of the test surface from the design's testing matrix)

**Step 11.1: Write failing tests for the behavior**

12 tests covering: throw allow / throw deny / Result allow / Result deny / multi-policy AND / stacking order / cancellation / pipeline ordering / eager-validation-missing-context / eager-validation-unregistered-policy / auto-registration / opt-out.

(Concrete code: each test sets up a minimal `ServiceCollection`, registers a tiny mediator setup with `WithAuthorization`, defines a one-off policy class, sends a request, asserts the outcome.)

**Step 11.2: Implement AuthorizationBehavior**

Single class, ~80 LOC. Resolves `ISecurityContext` and `IServiceProvider` via constructor. `Handle` calls `GeneratedAuthorizationLookup.GetPoliciesFor<TRequest>()`, iterates, evaluates, branches on `TResponse` shape via a runtime check (`typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<,>)`).

To keep this AOT-clean, the actual implementation will use a small static helper `Result.Of<TResponse>(AuthorizationFailure failure)` that returns either `default(TResponse)` (for non-Result paths — but actually throws, since the throw path doesn't return) or constructs the Result via reflection-free generic specialization. **Implementation detail: explore using two distinct behavior subclasses (one per path) registered conditionally based on whether `TResponse` is `Result<,>`.** Alternative: emit small per-request `if`/`else` shims from the generator. Settle in this task.

**Step 11.3: Wire WithAuthorization to auto-register policies via emitted DIExtensions class**

`MediatorAuthorizationServiceCollectionExtensions.WithAuthorization()` calls `GeneratedAuthorizationDIExtensions.RegisterDiscoveredPolicies(services)` unless `opts.AutoRegisterDiscoveredPolicies = false`. Use a static `[ModuleInitializer]` that the generator-emitted code populates so the host can find the DI extensions without reflection.

**Step 11.4: Run all tests, expect PASS** (12 passing).

```
git commit -m "feat(mediator.authorization): AuthorizationBehavior + eager validation + auto-registration"
```

---

## Task 12: AOT smoke + AllocationGate

**Files:**
- Create: `samples/ZeroAlloc.Mediator.AotSmoke/Authorization/AuthorizedScenario.cs` (new file, exercises both throw and Result paths)
- Create: `samples/ZeroAlloc.Mediator.AotSmoke/Internal/AllocationGate.cs` (copy from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs`, change namespace)
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/Program.cs` — call the new authorized scenario, then assert allocation budget for the four allow-paths.
- Modify: `samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj` — add `<ProjectReference>` to the new sub-package.
- Modify: `tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj` — add `<Compile Include Link>` to `AllocationGate.cs` for JIT-side gate tests.
- Add tests: `AllocationBudgetTests.cs` — 4 budget tests (throw allow, throw deny via anonymous, Result allow, IsAuthorizedAsync allow) + 3 self-tests (lifted from Authorization repo).

Same exact pattern as in `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization` PR #11. Repo-portable as designed.

```
git commit -m "feat(certify): AOT smoke + allocation gate for Mediator.Authorization"
```

---

## Task 13: README + push + open PR

**Files:**
- Modify: `README.md` (top-level — add a section advertising the new sub-package).
- Modify: `docs/dependency-injection.md` (or similar) — document `WithAuthorization()`.
- Optional: `docs/authorization.md` (new, if the topic warrants its own page).

**Step 13.1: Document the new sub-package**

Add to README's "Packages" table (or wherever sibling sub-packages are listed):

```
| ZeroAlloc.Mediator.Authorization | Pipeline behavior gating IRequest&lt;T&gt; via [Authorize]/[AuthorizationPolicy]. Source-generated per-request policy lookup. Throw-on-deny or Result&lt;T,F&gt;-on-deny via marker interface. |
```

Add a quick-start snippet:

```csharp
// Define a policy
[AuthorizationPolicy("AdminOnly")]
public sealed class AdminOnlyPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) => ctx.Roles.Contains("Admin");
}

// Decorate a request
[Authorize("AdminOnly")]
public sealed record DeleteUserCommand(string UserId) : IRequest;

// Wire up
services.AddMediator()
    .WithAuthorization(opts => opts.UseSecurityContextFactory(sp => /* your code */));
```

**Step 13.2: Push + open PR**

```
git push -u origin feat/mediator-authorization
gh pr create --base main --head feat/mediator-authorization \
  --title "feat(mediator.authorization): new sub-package — request-handler authz" \
  --body "$(cat <<'EOF'
## Summary

Adds ZeroAlloc.Mediator.Authorization as a 6th sub-package alongside Cache/Resilience/Telemetry/Validation. Adapts the ZeroAlloc.Authorization contract for IRequest<T> handler-gating via:

- **Single hand-coded** AuthorizationBehavior<TRequest, TResponse> — same shape as Mediator.Validation.
- **Roslyn generator** emits a per-compilation GeneratedAuthorizationLookup static class. No per-request behavior classes; the lookup is one switch on typeof(TRequest).
- **Dual-path failure semantics** — IRequest<T> + [Authorize] throws AuthorizationDeniedException; IAuthorizedRequest<T> + [Authorize] returns Result<T, AuthorizationFailure>. User picks per-request.

## Why this shape (vs alternatives)

The original brainstorming picked per-request generated IPipelineBehavior, on the argument that it was "the idiomatic Mediator extension shape." That argument was wrong. Looking at the actual sibling packages in this repo, **none of them generate per-request behaviors** — they all use a single behavior + per-request DI generic dispatch. We match that.

The host-side generator (vs waiting for Authorization backlog #5 to ship a contract-side generator) is deliberate: #5's stated graduation signal is "the second host is being built and the author finds themselves writing scan-and-register code." This sub-package IS that signal. When #5 ships, ~80 LOC of this generator gets deleted and replaced by AuthorizerFor<> resolution — straightforward migration with concrete evidence of the right shape.

## Strategy ramifications

This unblocks Authorization backlog items #1, #3, #5 — they're all gated on a second host surfacing real friction with stacking, resource-binding, and scan-and-register.

## Design + plan

- Design: docs/plans/2026-05-06-mediator-authorization-design.md
- Plan: docs/plans/2026-05-06-mediator-authorization.md

## Test plan

- [ ] CI build green: 12+ runtime tests pass under JIT.
- [ ] CI build green: snapshot tests pass for the 5 generator scenarios.
- [ ] CI aot-smoke green: AOT-published binary exercises both throw and Result paths, AllocationGate confirms 0B happy path.
- [ ] CI api-compat green: new public surface only, no break to existing.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 13.3: Watch CI**

```
gh pr checks --watch
```

Expected: lint-commits, build, aot-smoke, api-compat all green.

```
git commit -m "docs(mediator.authorization): README quick-start + dependency-injection section"
```

---

## Task 14: Cross-repo bookkeeping — Authorization backlog host-coupling notes

**Files (different repo):**
- Modify: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/docs/backlog.md`

For each of items #1–#5, add a "**Host coupling notes:**" subsection naming `Mediator.Authorization` and the bucket (transparent / generator update / runtime+DI). Reference the compatibility matrix from `Mediator.Authorization`'s design doc.

```
cd ../ZeroAlloc.Authorization
git checkout -b docs/host-coupling-notes main
# Edit docs/backlog.md
git add docs/backlog.md
git commit -m "docs(backlog): add host-coupling notes for items affecting Mediator.Authorization"
git push -u origin docs/host-coupling-notes
gh pr create --title "docs(backlog): flag host coupling for items affecting Mediator.Authorization" \
             --body "Cross-repo bookkeeping for ZeroAlloc.Mediator/feat/mediator-authorization. Future contract-package PRs should consult these notes before claiming an item is 'transparent' to the host."
```

---

## Done

After Tasks 1-14:

- New sub-package `ZeroAlloc.Mediator.Authorization` exists in this repo, alongside the rest of the Mediator family.
- One PR open here, CI green.
- One PR open on Authorization repo flagging host coupling.
- Once both PRs merge, the next Mediator-family release-please cycle ships `ZeroAlloc.Mediator.Authorization` to NuGet at the same family version (likely `4.1.0`).
- Authorization backlog items #1, #3, #5 unblocked — the second host exists, the friction is concrete.
