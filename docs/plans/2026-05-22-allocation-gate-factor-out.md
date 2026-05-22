# `AllocationGate` Factor-Out Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Factor `AllocationGate.cs` out of three independent copies (in `ZeroAlloc.Authorization`, `ZeroAlloc.Mediator`, `ZeroAlloc.Mapping`) into a new source-only NuGet package `ZeroAlloc.TestHelpers`, then migrate the three consumers to use it.

**Architecture:** New repo `ZeroAlloc-Net/ZeroAlloc.TestHelpers`. One source file at `src/ZeroAlloc.TestHelpers/AllocationGate.cs` packed via NuGet `contentFiles` (no DLL, no runtime payload). `DevelopmentDependency=true` + `SuppressDependenciesWhenPacking=true` keep the package off every consumer's runtime dependency graph. Each consumer adds a `PackageReference` with `PrivateAssets="all"` + `IncludeAssets="contentfiles;build"`; the source file compiles into their assembly as `internal static class AllocationGate` in namespace `ZeroAlloc.TestHelpers`.

**Tech Stack:** .NET SDK 10, NuGet contentFiles distribution, xUnit (4-test regression suite for the helper), release-please simple release-type, Microsoft.CodeAnalysis.PublicApiAnalyzers (RS0016/RS0017 against an empty public surface).

**Design doc:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/docs/plans/2026-05-22-allocation-gate-factor-out-design.md`

**Repos involved:**
- NEW: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers/` (to be created)
- CONSUMER: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/`
- CONSUMER: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/`
- CONSUMER: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mapping/`

**Key context to keep in mind:**

- The canonical helper content is what currently lives in `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/samples/ZeroAlloc.Mediator.AotSmoke/Internal/AllocationGate.cs` (modulo namespace) — Authorization is byte-identical; Mapping is missing the `AssertBudgetValueTask<T>` method.
- After this plan ships, all three consumers will have `AssertBudgetValueTask<T>` available even though Mapping doesn't currently use it. That's the drift closure.
- The new repo's workflows mirror the post-PR-#26 state of `ZeroAlloc.Authorization`'s `.github/workflows/` (walks `src/*/*.csproj`).

---

## Task 0: Open the design-doc PR against ZeroAlloc.Mediator

Already on disk (commit `b79c70a` on local branch `chore/design-allocation-gate-factor-out`). Push and open the PR so the design doc lands alongside backlog item #10.

**Step 1: Push the branch**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
git push -u origin chore/design-allocation-gate-factor-out
```

**Step 2: Open the PR**

```bash
gh pr create --title "docs(plans): design for AllocationGate factor-out into ZeroAlloc.TestHelpers" --body "$(cat <<'EOF'
## Summary

Design document for ZA.Mediator backlog item #10 (lift \`AllocationGate\` into a shared source-only package). Both halves of the dual graduation signal are met:

1. Three packages (Authorization, Mediator, Mapping) have independently copied the helper.
2. Mapping has drifted — it's missing \`AssertBudgetValueTask<T>\` that Authorization and Mediator added later.

The design lands the helper in a new repo \`ZeroAlloc-Net/ZeroAlloc.TestHelpers\` as a source-only NuGet package using \`contentFiles\` distribution. Implementation lands across 5 sequenced PRs: 1 bootstrap PR in the new repo + 3 consumer migration PRs + 1 small release-please cleanup PR.

## Test plan

- [ ] CI green (no code changes — design doc only)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3: Watch CI + admin-merge once green**

```bash
sleep 10
gh pr checks $(gh pr view --json number --jq .number)
# Once green:
gh pr merge $(gh pr view --json number --jq .number) --squash --admin --delete-branch
```

(Admin-bypass merge consistent with the session's prior pattern — small docs-only PR, no review-blocking value.)

---

## Task 1: Create the new GitHub repo + local skeleton

**Files (create new directory):**
- Create: `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers/` (empty directory + git init)

**Step 1: Create the GitHub repo**

```bash
gh repo create ZeroAlloc-Net/ZeroAlloc.TestHelpers \
  --public \
  --description "Source-distributed test helpers for the ZeroAlloc.* ecosystem (currently: AllocationGate, a zero-alloc assertion harness for AOT smokes and Tests)."
```

Expected: repo URL printed; no clone happens.

**Step 2: Initialize the local repo**

```bash
mkdir -p c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers
git init -b main
git remote add origin https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers.git
```

**Step 3: Add LICENSE, .gitignore, .gitattributes**

Copy the MIT LICENSE verbatim from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/LICENSE` (same copyright holder).

`.gitignore`: copy from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/.gitignore`.

`.gitattributes`: copy from `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/.gitattributes` if it exists; otherwise create with:

```
* text=auto eol=lf
*.cs   text eol=lf diff=csharp
*.csproj text eol=lf
*.props  text eol=lf
*.targets text eol=lf
*.yml  text eol=lf
*.md   text eol=lf
*.json text eol=lf
```

**Step 4: Initial commit (don't push yet)**

```bash
git add LICENSE .gitignore .gitattributes
git commit -m "chore: initial commit (LICENSE, .gitignore, .gitattributes)"
```

---

## Task 2: Add `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `README.md`, `CHANGELOG.md`

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `global.json`
- Create: `README.md`
- Create: `CHANGELOG.md`

**Step 1: `Directory.Build.props`** (adapted from ZA.Authorization):

```xml
<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <Authors>Marcel Roozekrans</Authors>
    <Company>Marcel Roozekrans</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers</PackageProjectUrl>
    <RepositoryUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <Copyright>Copyright (c) Marcel Roozekrans</Copyright>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors>$(WarningsAsErrors);RS0016;RS0017</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

**Step 2: `Directory.Packages.props`**:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Tests -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- Source-only package: no runtime deps. -->
    <PackageVersion Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="4.14.0" />
  </ItemGroup>
</Project>
```

**Step 3: `global.json`** (match ZA.Authorization's SDK pin):

```json
{
  "sdk": {
    "version": "10.0.202",
    "rollForward": "latestMinor"
  }
}
```

**Step 4: `README.md`**:

````markdown
# ZeroAlloc.TestHelpers

Source-distributed test helpers for the ZeroAlloc.* ecosystem.

## Usage

```xml
<PackageReference Include="ZeroAlloc.TestHelpers" Version="1.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>contentfiles;build</IncludeAssets>
</PackageReference>
```

The single file `AllocationGate.cs` compiles directly into your assembly under namespace `ZeroAlloc.TestHelpers` as `internal static class AllocationGate`. No runtime DLL dependency.

## API

- `AllocationGate.AssertBudget(int budgetBytes, int iterations, Action action, string label)` — runs `action` `iterations` times after a warmup + forced GC, throws `InvalidOperationException` if total allocations exceed `budgetBytes * iterations`.
- `AllocationGate.AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label)` — same shape for `ValueTask<T>`-returning APIs; throws if the supplied `ValueTask<T>` did not complete synchronously (awaiter machinery would pollute the measurement).

## Why source-only

Test infrastructure should never appear on a consumer package's public API surface. By compiling into the consumer's assembly as `internal`, the helper is per-consumer-scoped and zero-cost at consumer-runtime.
````

**Step 5: `CHANGELOG.md`**:

```markdown
# Changelog
```

(Empty stub; release-please will populate it on the first release.)

**Step 6: Commit**

```bash
git add Directory.Build.props Directory.Packages.props global.json README.md CHANGELOG.md
git commit -m "chore: add repo-level configs (Directory.Build.props, global.json, README)"
```

---

## Task 3: Add the source file + csproj

**Files:**
- Create: `src/ZeroAlloc.TestHelpers/AllocationGate.cs`
- Create: `src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj`
- Create: `src/ZeroAlloc.TestHelpers/PublicAPI.Shipped.txt`
- Create: `src/ZeroAlloc.TestHelpers/PublicAPI.Unshipped.txt`

**Step 1: `src/ZeroAlloc.TestHelpers/AllocationGate.cs`** (canonical version: identical to `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/samples/ZeroAlloc.Mediator.AotSmoke/Internal/AllocationGate.cs` modulo the namespace line):

```csharp
namespace ZeroAlloc.TestHelpers;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        // Warmup — JIT-compile, populate type-handle caches, allocate one-time fixtures.
        action();
        action();

        // Flush warmup garbage so it can't leak into the measurement.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        var totalBudget = (long)budgetBytes * iterations;
        if (allocated > totalBudget)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {allocated} B total over {iterations} iterations " +
                $"(~{perCall} B/call avg), budget is {budgetBytes} B/call ({totalBudget} B total). " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }

    public static void AssertBudgetValueTask<T>(int budgetBytes, int iterations, Func<ValueTask<T>> action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        static T Drain(ValueTask<T> t)
        {
            if (!t.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "AllocationGate: sync-completion-required — the supplied ValueTask did not " +
                    "complete synchronously. Awaiter machinery would pollute the measurement; " +
                    "the API under test must return an already-completed ValueTask.");
            }
            return t.Result;
        }

        // Warmup.
        Drain(action());
        Drain(action());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) Drain(action());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        var totalBudget = (long)budgetBytes * iterations;
        if (allocated > totalBudget)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {allocated} B total over {iterations} iterations " +
                $"(~{perCall} B/call avg), budget is {budgetBytes} B/call ({totalBudget} B total). " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }
}
```

**Step 2: `src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj`** (per design doc Section 2):

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
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Step 3: PublicAPI files** — both empty (no public surface):

```bash
touch src/ZeroAlloc.TestHelpers/PublicAPI.Shipped.txt
touch src/ZeroAlloc.TestHelpers/PublicAPI.Unshipped.txt
```

**Step 4: Verify build + pack**

```bash
dotnet build src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj -c Release
```

Expected: 0 warnings, 0 errors. (`netstandard2.0` target framework means the compilation goes through but `IncludeBuildOutput=false` discards the DLL.)

```bash
dotnet pack src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj -c Release -p:PackageVersion=1.0.0-localtest -o /tmp/za-th-pack
unzip -l /tmp/za-th-pack/ZeroAlloc.TestHelpers.1.0.0-localtest.nupkg
```

Expected files in the nupkg:
- `contentFiles/cs/any/ZeroAlloc.TestHelpers/AllocationGate.cs`
- `ZeroAlloc.TestHelpers.nuspec`
- `[Content_Types].xml`, `_rels/.rels`, package metadata
- NO `lib/` folder.

Verify the nuspec contains `<developmentDependency>true</developmentDependency>` and an empty (or absent) `<dependencies>`:

```bash
unzip -p /tmp/za-th-pack/ZeroAlloc.TestHelpers.1.0.0-localtest.nupkg ZeroAlloc.TestHelpers.nuspec | grep -E "developmentDependency|<dependencies"
```

**Step 5: Commit**

```bash
git add src/ZeroAlloc.TestHelpers/
git commit -m "feat: AllocationGate source-distributed via contentFiles

namespace ZeroAlloc.TestHelpers, internal static class. Packed at
contentFiles/cs/any/ZeroAlloc.TestHelpers/AllocationGate.cs so consumer
PackageReferences with IncludeAssets=\"contentfiles;build\" compile the
file into their own assembly.

DevelopmentDependency=true + SuppressDependenciesWhenPacking=true keep
the package off every consumer's runtime dependency graph."
```

---

## Task 4: Add the test project

**Files:**
- Create: `tests/ZeroAlloc.TestHelpers.Tests/ZeroAlloc.TestHelpers.Tests.csproj`
- Create: `tests/ZeroAlloc.TestHelpers.Tests/AllocationGateTests.cs`

**Step 1: `tests/ZeroAlloc.TestHelpers.Tests/ZeroAlloc.TestHelpers.Tests.csproj`**:

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

**Step 2: `tests/ZeroAlloc.TestHelpers.Tests/AllocationGateTests.cs`** — verbatim:

```csharp
using System;
using System.Threading.Tasks;
using Xunit;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.TestHelpers.Tests;

public sealed class AllocationGateTests
{
    [Fact]
    public void AssertBudget_NoAllocation_DoesNotThrow()
    {
        long counter = 0;
        AllocationGate.AssertBudget(
            budgetBytes: 0,
            iterations: 100,
            action: () => { counter++; },
            label: "zero-alloc counter increment");

        Assert.Equal(102, counter); // 2 warmup + 100 measured
    }

    [Fact]
    public void AssertBudget_OverBudget_ThrowsWithDiagnosticMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudget(
                budgetBytes: 0,
                iterations: 10,
                action: () => _ = new byte[1024], // 1 KiB per call
                label: "deliberate allocator"));

        Assert.Contains("deliberate allocator", ex.Message, StringComparison.Ordinal);
        Assert.Contains("budget is 0 B/call", ex.Message, StringComparison.Ordinal);
        Assert.Contains("MemoryDiagnoser", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssertBudgetValueTask_SyncCompleted_NoAllocation_DoesNotThrow()
    {
        long counter = 0;
        AllocationGate.AssertBudgetValueTask(
            budgetBytes: 0,
            iterations: 100,
            action: () => { counter++; return new ValueTask<int>(42); },
            label: "zero-alloc sync-completed ValueTask");

        Assert.Equal(102, counter);
    }

    [Fact]
    public void AssertBudgetValueTask_AsyncCompletion_ThrowsSyncCompletionRequired()
    {
        async ValueTask<int> AsyncBody()
        {
            await Task.Yield(); // forces async completion
            return 42;
        }

        var ex = Assert.Throws<InvalidOperationException>(() =>
            AllocationGate.AssertBudgetValueTask<int>(
                budgetBytes: 1024,
                iterations: 10,
                action: AsyncBody,
                label: "async ValueTask"));

        Assert.Contains("sync-completion-required", ex.Message, StringComparison.Ordinal);
    }
}
```

**Step 3: Run the tests**

```bash
dotnet test tests/ZeroAlloc.TestHelpers.Tests -c Release
```

Expected: 4 passed, 0 failed.

**Step 4: Commit**

```bash
git add tests/
git commit -m "test: AllocationGate self-tests (4 facts)

Verifies the helper's contract end-to-end:
  - zero-alloc Action passes
  - allocating Action throws with diagnostic message
  - sync-completed ValueTask passes
  - non-sync ValueTask trips the sync-completion guard

The test project references the source file via <Compile Include> rather
than the package's contentFiles transform. The migration PRs in
consumer repos serve as the integration test for the contentFiles path."
```

---

## Task 5: Add release-please config + manifest + workflows

**Files:**
- Create: `release-please-config.json`
- Create: `.release-please-manifest.json`
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/release-please.yml`
- Create: `.github/workflows/publish-from-manifest.yml`
- Create: `.commitlintrc.yml`

**Step 1: `release-please-config.json`**:

```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "packages": {
    ".": {
      "release-type": "simple",
      "release-as": "1.0.0",
      "changelog-sections": [
        { "type": "feat",     "section": "Features" },
        { "type": "fix",      "section": "Bug Fixes" },
        { "type": "docs",     "section": "Documentation" },
        { "type": "refactor", "section": "Refactors" }
      ]
    }
  }
}
```

**Step 2: `.release-please-manifest.json`**:

```json
{".":"0.0.0"}
```

**Step 3: `.commitlintrc.yml`** (copy verbatim from ZA.Authorization):

```bash
cp c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/.commitlintrc.yml .commitlintrc.yml
```

**Step 4: `.github/workflows/ci.yml`** — adapted from ZA.Authorization (the AOT-smoke job is removed; api-compat is kept with the empty-surface stays empty contract):

```yaml
name: CI

on:
  push:
    branches: [main, 'release-please--**']
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  lint-commits:
    runs-on: ubuntu-latest
    if: github.event_name == 'pull_request'
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Lint commit messages
        uses: wagoid/commitlint-github-action@v6
        with:
          configFile: .commitlintrc.yml

  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Restore
        run: dotnet restore
      - name: Build
        run: dotnet build --configuration Release --no-restore
      - name: Test
        run: dotnet test --configuration Release --no-build --verbosity normal
      - name: Pack (verify)
        run: dotnet pack src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj --configuration Release --no-build --output ./artifacts
      - name: Upload NuGet artifact
        uses: actions/upload-artifact@v4
        with:
          name: nuget-package
          path: ./artifacts/*.nupkg

  api-compat:
    uses: ZeroAlloc-Net/.github/.github/workflows/api-compat.yml@main
    with:
      package-id: ZeroAlloc.TestHelpers
      csproj-path: src/ZeroAlloc.TestHelpers/ZeroAlloc.TestHelpers.csproj
```

(No AOT smoke — the helper itself doesn't need to certify under AOT; its consumers' smokes do that work transitively.)

**Step 5: `.github/workflows/release-please.yml`** — mirrors the post-PR-#26 ZA.Authorization version (walks `src/*/*.csproj`):

```yaml
name: Release Please

on:
  push:
    branches: [main]

permissions:
  contents: write
  pull-requests: write
  packages: write

jobs:
  release-please:
    runs-on: ubuntu-latest
    outputs:
      releases_created: ${{ steps.release.outputs.releases_created }}
      tag_name: ${{ steps.release.outputs.tag_name }}
      version: ${{ steps.release.outputs.version }}
    steps:
      - uses: googleapis/release-please-action@v5
        id: release
        with:
          config-file: release-please-config.json
          manifest-file: .release-please-manifest.json
          token: ${{ secrets.RELEASE_PLEASE_TOKEN }}

  publish:
    needs: release-please
    if: needs.release-please.outputs.releases_created == 'true'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ needs.release-please.outputs.tag_name }}
          fetch-depth: 0
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Build
        run: dotnet build --configuration Release -p:Version=${{ needs.release-please.outputs.version }}
      - name: Test
        run: dotnet test --configuration Release --no-build --verbosity normal
      - name: Pack every src/* csproj
        env:
          VERSION: ${{ needs.release-please.outputs.version }}
        run: |
          mkdir -p ./artifacts
          for csproj in src/*/*.csproj; do
            echo "Packing $csproj at version $VERSION"
            dotnet pack "$csproj" --configuration Release --no-build \
              --output ./artifacts -p:Version="$VERSION"
          done
          ls -la ./artifacts
      - name: Push to NuGet
        run: dotnet nuget push ./artifacts/*nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }} --skip-duplicate
      - name: Push to GitHub Packages
        run: dotnet nuget push ./artifacts/*nupkg --source https://nuget.pkg.github.com/ZeroAlloc-Net/index.json --api-key ${{ secrets.GITHUB_TOKEN }} --skip-duplicate
```

**Step 6: `.github/workflows/publish-from-manifest.yml`** — rescue workflow (copy verbatim from ZA.Authorization):

```bash
cp c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Authorization/.github/workflows/publish-from-manifest.yml \
   .github/workflows/publish-from-manifest.yml
```

**Step 7: Commit**

```bash
git add release-please-config.json .release-please-manifest.json .commitlintrc.yml .github/
git commit -m "ci: release-please + CI + publish-from-manifest workflows

Adapts the post-PR-#26 ZA.Authorization workflow conventions:
  - release-please.yml walks src/*/*.csproj (multi-package-safe).
  - publish-from-manifest.yml is the rescue workflow following the same
    walking pattern.
  - ci.yml drops the AOT-smoke job (this repo's helper is consumed by
    other repos' smokes; self-referential smoke would not add signal)
    but keeps api-compat against the empty public surface.

Initial release pinned via release-as: \"1.0.0\". Override is dropped
in a small followup PR after the first publish verifies."
```

---

## Task 6: Open the initial PR; merge; verify v1.0.0 publishes

**Step 1: Sanity check the commit history**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers
git log --oneline
```

Expected (5 commits, newest first):
1. `ci: release-please + CI + publish-from-manifest workflows`
2. `test: AllocationGate self-tests (4 facts)`
3. `feat: AllocationGate source-distributed via contentFiles`
4. `chore: add repo-level configs (Directory.Build.props, global.json, README)`
5. `chore: initial commit (LICENSE, .gitignore, .gitattributes)`

**Step 2: Push to `main` directly** (special-case for a brand-new repo; branch-protection rules don't exist yet)

```bash
git push -u origin main
```

**Step 3: Wait for Release Please to open the v1.0.0 PR**

```bash
sleep 30
gh pr list --state open
```

Expected: one PR `chore(main): release 1.0.0` opened by release-please action.

**Step 4: Verify CI green on `main` push**

```bash
gh run list --workflow=ci.yml --limit 1
gh run watch $(gh run list --workflow=ci.yml --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

Expected: build + test + api-compat all pass.

**Step 5: Admin-merge the release-please PR**

```bash
gh pr merge $(gh pr list --state open --json number --jq '.[0].number') --squash --admin
```

**Step 6: Watch Release Please's publish job + verify v1.0.0 on nuget.org**

```bash
sleep 20
gh run list --workflow=release-please.yml --limit 2
# Wait for the publish job to finish
gh run watch $(gh run list --workflow=release-please.yml --branch=main --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status

# Confirm on nuget.org (allow ~3 min propagation)
sleep 180
curl -sI "https://api.nuget.org/v3-flatcontainer/zeroalloc.testhelpers/1.0.0/zeroalloc.testhelpers.1.0.0.nupkg" | head -1
```

Expected: `HTTP/1.1 200 OK`.

If the version index doesn't show 1.0.0 yet, wait another 2-3 minutes; the flatcontainer index lags the per-blob endpoint by a small amount.

---

## Task 7: Migrate `ZeroAlloc.Mediator` consumer

**Repo:** `c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator/`

**Branch:** `chore/migrate-to-zeroalloc-test-helpers`

**Files to modify:**
- `samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj` — add PackageReference.
- `tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj` — add PackageReference (AllocationBudgetTests references the smoke's Internal namespace).
- `samples/ZeroAlloc.Mediator.AotSmoke/Internal/AllocationGate.cs` — DELETE.
- All `.cs` files with `using ZeroAlloc.Mediator.AotSmoke.Internal;` → replace with `using ZeroAlloc.TestHelpers;`.

**Step 1: Branch + add PackageReference**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Mediator
git fetch origin && git checkout main && git pull --ff-only
git checkout -b chore/migrate-to-zeroalloc-test-helpers
```

Edit `samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj`. Find the `<ItemGroup>` with `PackageReference` items (alongside any test/AOT-related deps). Add:

```xml
<PackageReference Include="ZeroAlloc.TestHelpers" Version="1.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>contentfiles;build</IncludeAssets>
</PackageReference>
```

Repeat for `tests/ZeroAlloc.Mediator.Authorization.Tests/ZeroAlloc.Mediator.Authorization.Tests.csproj`.

**Step 2: Delete the local copy**

```bash
rm samples/ZeroAlloc.Mediator.AotSmoke/Internal/AllocationGate.cs
# Also remove the (now empty) Internal/ folder if no other files exist there
rmdir samples/ZeroAlloc.Mediator.AotSmoke/Internal 2>/dev/null || true
```

**Step 3: Find and replace `using` directives**

```bash
grep -rln "using ZeroAlloc.Mediator.AotSmoke.Internal;" samples/ tests/
```

For each file returned, replace `using ZeroAlloc.Mediator.AotSmoke.Internal;` with `using ZeroAlloc.TestHelpers;`. On Windows PowerShell or Bash:

```bash
grep -rl "using ZeroAlloc.Mediator.AotSmoke.Internal;" samples/ tests/ \
  | xargs sed -i 's|using ZeroAlloc.Mediator.AotSmoke.Internal;|using ZeroAlloc.TestHelpers;|g'
```

**Step 4: Verify build + tests + AOT publish**

```bash
dotnet build samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release
dotnet test tests/ZeroAlloc.Mediator.Authorization.Tests -c Release
dotnet publish samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -c Release -p:PublishAot=true -o ./aot-out/AotSmoke
./aot-out/AotSmoke/ZeroAlloc.Mediator.AotSmoke
```

Expected: all green; AOT binary prints `AOT smoke: PASS`.

**Step 5: Commit + push + open PR**

```bash
git add samples/ tests/
git commit -m "chore(deps): migrate to ZeroAlloc.TestHelpers for AllocationGate

Removes the local samples/.../Internal/AllocationGate.cs and pulls the
helper in via the new ZeroAlloc.TestHelpers package
(ZeroAlloc-Net/ZeroAlloc.TestHelpers, source-only NuGet at
contentFiles/cs/any/ZeroAlloc.TestHelpers/AllocationGate.cs).

Consumer recipe per the package's README: PrivateAssets=\"all\" +
IncludeAssets=\"contentfiles;build\" on the PackageReference. The source
file compiles into the AotSmoke + Tests assemblies as internal static
class AllocationGate in namespace ZeroAlloc.TestHelpers.

Closes the AllocationGate-copy-drift across the ZeroAlloc.* ecosystem."
git push -u origin chore/migrate-to-zeroalloc-test-helpers
gh pr create --title "chore(deps): migrate to ZeroAlloc.TestHelpers for AllocationGate" --body "$(cat <<'EOF'
## Summary

Removes the local copy of \`samples/.../Internal/AllocationGate.cs\` and replaces it with a \`PackageReference\` to the newly-published \`ZeroAlloc.TestHelpers\` 1.0.0 package.

Part of the rollout for ZA.Mediator backlog item #10.

## Test plan

- [ ] CI green: build + tests (31 tests still pass) + aot-smoke + api-compat
- [ ] AOT publish + run of the smoke binary succeeds with the new package

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 6: Watch CI; admin-merge when green**

```bash
sleep 10
gh pr checks $(gh pr view --json number --jq .number)
# Once green:
gh pr merge $(gh pr view --json number --jq .number) --squash --admin --delete-branch
```

---

## Task 8: Migrate `ZeroAlloc.Authorization` consumer

Same shape as Task 7, in the ZA.Authorization repo. Differences:

- Files to update: `samples/ZeroAlloc.Authorization.AotSmoke/ZeroAlloc.Authorization.AotSmoke.csproj` (add PackageReference); `samples/ZeroAlloc.Authorization.AotSmoke/Internal/AllocationGate.cs` (delete); all `.cs` files with `using ZeroAlloc.Authorization.AotSmoke.Internal;` → `using ZeroAlloc.TestHelpers;`.
- No test project change (this repo doesn't have a test that imports the helper — verify by `grep -rln "AllocationGate" tests/` before assuming).
- Branch: `chore/migrate-to-zeroalloc-test-helpers`.
- Commit + PR template identical to Task 7 modulo s/Mediator/Authorization/.

---

## Task 9: Migrate `ZeroAlloc.Mapping` consumer

Same shape as Task 7. Differences:

- Files: `samples/ZeroAlloc.Mapping.AotSmoke/ZeroAlloc.Mapping.AotSmoke.csproj` + `samples/.../Internal/AllocationGate.cs` (delete) + `using` replacements.
- Branch: `chore/migrate-to-zeroalloc-test-helpers`.
- **Special note in the commit body:** mention that Mapping inherits `AssertBudgetValueTask<T>` automatically (it was missing from Mapping's local copy). The Mapping smoke doesn't currently call it; the capability is just newly available.

---

## Task 10: Drop the `release-as` override in `ZeroAlloc.TestHelpers`

Final hygiene step. v1.0.0 has shipped; remove the one-shot override so conventional-commit bumps work from 1.0.1 onward.

**Step 1: Branch + edit**

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.TestHelpers
git fetch origin && git checkout main && git pull --ff-only
git checkout -b chore/drop-release-as-override
```

Edit `release-please-config.json` and remove the `"release-as": "1.0.0",` line.

**Step 2: Commit + push + open PR**

```bash
git add release-please-config.json
git commit -m "chore(release): drop one-shot release-as override

1.0.0 has shipped; conventional-commit-driven bumps are correct from here."
git push -u origin chore/drop-release-as-override
gh pr create --title "chore(release): drop one-shot release-as override" --body "$(cat <<'EOF'
## Summary

Removes the \`\"release-as\": \"1.0.0\"\` override in \`release-please-config.json\` now that 1.0.0 has shipped. Future conventional-commit-driven bumps will move the version normally.

## Test plan

- [ ] CI green (config-only change)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 3: Watch CI; admin-merge when green**

```bash
sleep 10
gh pr checks $(gh pr view --json number --jq .number)
# Once green:
gh pr merge $(gh pr view --json number --jq .number) --squash --admin --delete-branch
```

---

## Verification checklist (end-of-flow)

- [ ] `ZeroAlloc.TestHelpers` 1.0.0 + 1.0.1 (release-as drop) published to nuget.org.
- [ ] All three consumer repos (Mediator, Authorization, Mapping) have:
  - [ ] No local `samples/.../Internal/AllocationGate.cs` file.
  - [ ] `<PackageReference Include="ZeroAlloc.TestHelpers" ... />` in the appropriate csproj.
  - [ ] `using ZeroAlloc.TestHelpers;` in every file that calls `AllocationGate`.
  - [ ] AOT smoke + tests still pass.
- [ ] `docs/backlog.md` in ZA.Mediator: item #10 marked `✅ shipped`.

## Out of scope (per design doc)

- Other test helpers beyond `AllocationGate`.
- Task<T>-returning overload (`AssertBudgetTask<T>`).
- Migration of any repo other than the three current copies.
- Bootstrapping a `samples/` AOT smoke in `ZeroAlloc.TestHelpers` itself (the helper certifies under AOT transitively through consumer smokes).
