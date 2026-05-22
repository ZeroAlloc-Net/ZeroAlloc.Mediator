# `AllocationGate` factor-out into `ZeroAlloc.TestHelpers`

Date: 2026-05-22
Status: Approved
Backlog: graduates ZA.Mediator backlog item #10
Scope: new repo `ZeroAlloc.TestHelpers` + migration of 3 consumer repos

## Problem

Three packages in the ZeroAlloc.* ecosystem ship near-identical copies of
`samples/.../Internal/AllocationGate.cs`:

- `ZeroAlloc.Authorization/samples/.../Internal/AllocationGate.cs`
- `ZeroAlloc.Mediator/samples/.../Internal/AllocationGate.cs`
- `ZeroAlloc.Mapping/samples/.../Internal/AllocationGate.cs`

Authorization and Mediator are byte-identical (modulo namespace). Mapping is
structurally smaller — it has only the synchronous `AssertBudget(...)` method
and is missing `AssertBudgetValueTask<T>(...)` that Authorization and Mediator
added later. A maintainer touching the ValueTask path in two repos has no
indication that the third copy is silently different.

The ZA.Mediator backlog item #10 set a dual graduation signal: ship the
factor-out when (a) at least 3 packages have copied the helper independently
AND (b) a meaningful drift / fix has happened. Both halves are met.

## Goals

- One source of truth for `AllocationGate`.
- Distribution mechanism that keeps the helper `internal` to each consumer's
  assembly (test infrastructure should never leak into a consumer's public
  API surface).
- No runtime payload — the helper compiles into the consumer's assembly via
  NuGet `contentFiles`, not via a referenced DLL.
- No transitive flow — the package is `DevelopmentDependency=true` and
  consumers add `PrivateAssets="all"`. Reaching the helper from a downstream
  consumer's assembly is not a supported scenario.

## Design

### New repo: `ZeroAlloc.TestHelpers`

Standard ZA.* layout. The only "production" artifact is a single
source-distributed `.cs` file.

```
ZeroAlloc.TestHelpers/
├── src/ZeroAlloc.TestHelpers/
│   ├── ZeroAlloc.TestHelpers.csproj
│   ├── AllocationGate.cs                 (namespace ZeroAlloc.TestHelpers, internal static)
│   ├── PublicAPI.Shipped.txt             (empty)
│   └── PublicAPI.Unshipped.txt           (empty)
├── tests/ZeroAlloc.TestHelpers.Tests/
│   ├── ZeroAlloc.TestHelpers.Tests.csproj
│   └── AllocationGateTests.cs
├── docs/
│   ├── README.md
│   └── plans/2026-05-22-allocation-gate-factor-out-design.md
├── .github/workflows/{ci,release-please,publish-from-manifest}.yml
├── release-please-config.json
├── .release-please-manifest.json
├── Directory.Build.props
├── Directory.Packages.props
├── README.md
├── CHANGELOG.md
├── LICENSE
└── global.json
```

The PublicAPI analyzer is enabled even with an empty public surface, so
adding a `public` member by accident later fails the build.

No AOT smoke binary. The helper is consumed by other repos' AOT smokes;
a self-referential smoke would not add signal.

### Source file shape

`src/ZeroAlloc.TestHelpers/AllocationGate.cs` is the union of the current
two-method shape (the latest version that lives in Authorization and Mediator):

```csharp
namespace ZeroAlloc.TestHelpers;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label) { ... }
    public static void AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label) { ... }
}
```

`internal static class` is correct for source distribution: the file is
compiled into each consumer's assembly, so `internal` scope keeps the type
out of every consumer's public API surface.

### Source-only NuGet packaging

`src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>ZeroAlloc.TestHelpers</PackageId>
    <Description>Source-distributed test helpers for the ZeroAlloc.* ecosystem. Currently: AllocationGate (zero-alloc assertion harness for AOT smokes + Tests).</Description>

    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <DevelopmentDependency>true</DevelopmentDependency>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <IncludeSymbols>false</IncludeSymbols>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="AllocationGate.cs"
             Pack="true"
             PackagePath="contentFiles/cs/any/ZeroAlloc.TestHelpers/AllocationGate.cs"
             BuildAction="Compile"
             CopyToOutput="false" />
  </ItemGroup>
  <ItemGroup>
    <AdditionalFiles Include="PublicAPI.Shipped.txt" />
    <AdditionalFiles Include="PublicAPI.Unshipped.txt" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

`contentFiles/cs/any/...` is the NuGet-defined path for source-distributed
C# files (language = `cs`, TFM = `any`). `BuildAction="Compile"` tells NuGet
to add the file to the consumer's `@(Compile)` itemset so it's compiled into
the consumer's assembly directly. `CopyToOutput="false"` keeps the source
file out of consumer `bin/`.

### Consumer-side recipe

```xml
<PackageReference Include="ZeroAlloc.TestHelpers" Version="1.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>contentfiles;build</IncludeAssets>
</PackageReference>
```

`IncludeAssets="contentfiles;build"` is the load-bearing pair: `contentfiles`
brings in the source file; `build` brings any future `.props/.targets` we
might add (none today, but the pattern is portable). `PrivateAssets="all"`
is belt-and-suspenders with the package's own `DevelopmentDependency=true`.

Each consumer migration:

1. Add the `PackageReference` above.
2. Delete the local `samples/.../Internal/AllocationGate.cs`.
3. Replace `using ZeroAlloc.{Repo}.AotSmoke.Internal;` with
   `using ZeroAlloc.TestHelpers;` in every file that calls `AllocationGate`.
4. Build + AOT publish + run the smoke to confirm parity.

### Tests for the helper

`tests/ZeroAlloc.TestHelpers.Tests/ZeroAlloc.TestHelpers.Tests.csproj`
references the helper directly via `<Compile Include>` — same observable
file as the contentFiles export, just included rather than packaged.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\src\ZeroAlloc.TestHelpers\AllocationGate.cs"
             Link="AllocationGate.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
</Project>
```

`AllocationGateTests.cs` covers four scenarios:

1. `AssertBudget_NoAllocation_DoesNotThrow` — zero-alloc Action passes.
2. `AssertBudget_OverBudget_ThrowsWithDiagnosticMessage` — allocating Action throws.
3. `AssertBudgetValueTask_SyncCompleted_NoAllocation_DoesNotThrow` — zero-alloc ValueTask passes.
4. `AssertBudgetValueTask_AsyncCompletion_ThrowsSyncCompletionRequired` — guard fires on non-sync completion.

### Release plumbing

Mirrors the cleaned-up ZA.Authorization conventions:

- `release-please-config.json` — single component, `release-as: "1.0.0"` for the initial publish.
- `.release-please-manifest.json` — `{".":"0.0.0"}` initially; release-please bumps it to 1.0.0 on first merge.
- `.github/workflows/release-please.yml` — same shape as post-PR-#26 ZA.Authorization version (walks `src/*/*.csproj`, packs everything not marked `IsPackable=false`, pushes to NuGet + GitHub Packages).
- `.github/workflows/publish-from-manifest.yml` — rescue workflow following the same pattern.
- `.github/workflows/ci.yml` — `dotnet build` + `dotnet test` on push/PR.

Followup PR after v1.0.0 publishes drops the `release-as` override (same hygiene as ZA.Authorization #26 and ZA.Mediator #92).

## Sequencing

5 PRs total, ordered:

1. **`ZeroAlloc.TestHelpers` bootstrap PR** — repo creation, source file, tests, workflows, release-please. Merging triggers v1.0.0 publish.
2. **(wait for nuget.org propagation, ~5 min)**
3. **`ZeroAlloc.Mediator` consumer PR** — migrate first; two csprojs to update (smoke + tests, both reference the helper today via `using ZeroAlloc.Mediator.AotSmoke.Internal`). Largest test signal (31 tests).
4. **`ZeroAlloc.Authorization` consumer PR** — migrate second.
5. **`ZeroAlloc.Mapping` consumer PR** — migrate last; closes the existing drift by acquiring `AssertBudgetValueTask<T>` (no consumer code changes there, the capability just becomes available).
6. **Followup PR in `ZeroAlloc.TestHelpers`** — drop `release-as: "1.0.0"` override.

## Risks considered

- **NuGet `contentFiles` transform varies by SDK version.** Assumed SDK 10.x (already required across the org via `global.json`). Older-SDK consumers would need a different PackageReference syntax.
- **Source-file name collision.** If any consumer happens to have another type called `AllocationGate` in another namespace, an unqualified call could become ambiguous. Mitigated by `internal` scope + the `using ZeroAlloc.TestHelpers;` directive added in each migration PR, with the consumer's local file deleted in the same PR.
- **Missing `<PackageReference>` flags on the consumer side.** `contentfiles` is NOT in the default `IncludeAssets`; consumers that forget it get no source file and a CS0103 ("name does not exist") on the first call to `AllocationGate.AssertBudget`. Loud, immediate, easy to diagnose.
- **Self-dogfooding.** The test project references the source file via `<Compile Include>` rather than via the package's own contentFiles transform. This means the package's own tests do not exercise the contentFiles path. The three migration PRs are the actual integration test for that path; if any of them fail to find the helper after PackageReference, that catches a real packaging bug.

## Out of scope

- Other test helpers (no `EventuallyAssert`, no retry helpers, no `WithRetry`).
  Add only when concrete demand surfaces.
- A `Task<T>`-returning overload of `AssertBudget`. Not currently needed —
  every ZA.* hot-path API returns `ValueTask<T>` already. Add when a real
  consumer needs it.
- Migrating any other repo. Authorization, Mediator, Mapping are the three
  current copies; any future consumer just adds the new PackageReference.
