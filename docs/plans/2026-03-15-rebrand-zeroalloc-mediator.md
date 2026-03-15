# ZeroAlloc.Mediator Rebrand Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename the project from `ZMediator` to `ZeroAlloc.Mediator` — folders, project files, namespaces, type names, diagnostic IDs, CI/CD, and README.

**Architecture:** Mechanical rename only — no behavioural changes. The core library namespace becomes `ZeroAlloc` (flat, shared across all future ZeroAlloc packages). The generator package namespace stays `ZeroAlloc.Mediator.Generator` (internal, never user-facing). All public DI types are cleaned up: `IZMediator` → `IMediator`, `ZMediatorService` → `MediatorService`.

**Tech Stack:** .NET 10, C# 13, Roslyn incremental source generator, xUnit, BenchmarkDotNet, GitHub Actions

---

### Task 1: Rename folders with git mv

**Files:**
- Rename: `src/ZMediator/` → `src/ZeroAlloc.Mediator/`
- Rename: `src/ZMediator.Generator/` → `src/ZeroAlloc.Mediator.Generator/`
- Rename: `tests/ZMediator.Tests/` → `tests/ZeroAlloc.Mediator.Tests/`
- Rename: `tests/ZMediator.Benchmarks/` → `tests/ZeroAlloc.Mediator.Benchmarks/`
- Rename: `samples/ZMediator.Sample/` → `samples/ZeroAlloc.Mediator.Sample/`

**Step 1: Move all project folders**

```bash
git mv src/ZMediator src/ZeroAlloc.Mediator
git mv src/ZMediator.Generator src/ZeroAlloc.Mediator.Generator
git mv tests/ZMediator.Tests tests/ZeroAlloc.Mediator.Tests
git mv tests/ZMediator.Benchmarks tests/ZeroAlloc.Mediator.Benchmarks
git mv samples/ZMediator.Sample samples/ZeroAlloc.Mediator.Sample
```

**Step 2: Rename csproj files inside their new folders**

```bash
git mv src/ZeroAlloc.Mediator/ZMediator.csproj src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj
git mv src/ZeroAlloc.Mediator.Generator/ZMediator.Generator.csproj src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj
git mv tests/ZeroAlloc.Mediator.Tests/ZMediator.Tests.csproj tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj
git mv tests/ZeroAlloc.Mediator.Benchmarks/ZMediator.Benchmarks.csproj tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj
git mv samples/ZeroAlloc.Mediator.Sample/ZMediator.Sample.csproj samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj
```

**Step 3: Rename the solution file**

```bash
git mv ZMediator.slnx ZeroAlloc.Mediator.slnx
```

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: rename all project folders and files to ZeroAlloc.Mediator"
```

---

### Task 2: Update the solution file

**Files:**
- Modify: `ZeroAlloc.Mediator.slnx`

**Step 1: Replace all project paths in the solution**

Replace the content of `ZeroAlloc.Mediator.slnx` with:

```xml
<Solution>
  <Folder Name="/samples/">
    <Project Path="samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj" />
    <Project Path="src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj" />
    <Project Path="tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj" />
  </Folder>
</Solution>
```

**Step 2: Commit**

```bash
git add ZeroAlloc.Mediator.slnx
git commit -m "refactor: update solution file for ZeroAlloc.Mediator"
```

---

### Task 3: Update csproj files

**Files:**
- Modify: `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj`
- Modify: `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj`
- Modify: `tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj`
- Modify: `tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj`
- Modify: `samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj`

**Step 1: Update `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>ZeroAlloc</RootNamespace>
    <PackageId>ZeroAlloc.Mediator</PackageId>
  </PropertyGroup>
</Project>
```

**Step 2: Update `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>ZeroAlloc.Mediator.Generator</RootNamespace>
    <PackageId>ZeroAlloc.Mediator.Generator</PackageId>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <NoWarn>$(NoWarn);RS2008</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 3: Update `tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ZeroAlloc.Mediator.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.Testing.Verifiers.XUnit" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator\ZeroAlloc.Mediator.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator.Generator\ZeroAlloc.Mediator.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true" />
  </ItemGroup>
</Project>
```

**Step 4: Update `tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>ZeroAlloc.Mediator.Benchmarks</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.*" />
    <PackageReference Include="MediatR" Version="14.1.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.3" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator\ZeroAlloc.Mediator.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator.Generator\ZeroAlloc.Mediator.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Step 5: Update `samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>ZeroAlloc.Mediator.Sample</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator\ZeroAlloc.Mediator.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Mediator.Generator\ZeroAlloc.Mediator.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.3" />
  </ItemGroup>
</Project>
```

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: update all csproj PackageId, RootNamespace, and ProjectReference paths"
```

---

### Task 4: Update core library namespaces

**Files:**
- Modify: all `*.cs` in `src/ZeroAlloc.Mediator/` (10 files)

All files currently declare `namespace ZMediator`. Change every one to `namespace ZeroAlloc`.

Files to update (all with the same change):
- `IRequest.cs`
- `IRequestHandler.cs`
- `INotification.cs`
- `INotificationHandler.cs`
- `IStreamRequest.cs`
- `IStreamRequestHandler.cs`
- `IPipelineBehavior.cs`
- `ParallelNotificationAttribute.cs`
- `PipelineBehaviorAttribute.cs`
- `Unit.cs`

**Step 1: Rename namespace in all core library files**

For each file: change `namespace ZMediator` → `namespace ZeroAlloc`

**Step 2: Verify the build compiles (partial check)**

```bash
dotnet build src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj
```

Expected: no errors (tests may still fail — that's fine at this stage).

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Mediator/
git commit -m "refactor: rename core library namespace ZMediator -> ZeroAlloc"
```

---

### Task 5: Update generator info record files

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/RequestHandlerInfo.cs`
- Modify: `src/ZeroAlloc.Mediator.Generator/NotificationHandlerInfo.cs`
- Modify: `src/ZeroAlloc.Mediator.Generator/StreamHandlerInfo.cs`
- Modify: `src/ZeroAlloc.Mediator.Generator/PipelineBehaviorInfo.cs`
- Modify: `src/ZeroAlloc.Mediator.Generator/RequestTypeInfo.cs`

**Step 1: In each file, change the namespace declaration**

```csharp
// Before
namespace ZMediator.Generator

// After
namespace ZeroAlloc.Mediator.Generator
```

**Step 2: Commit**

```bash
git add src/ZeroAlloc.Mediator.Generator/
git commit -m "refactor: rename generator info record namespaces"
```

---

### Task 6: Update DiagnosticDescriptors

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/DiagnosticDescriptors.cs`

**Step 1: Update namespace, diagnostic IDs, and category**

```csharp
#nullable enable
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Generator
{
    internal static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor NoHandler = new DiagnosticDescriptor(
            "ZAM001",
            "No registered handler",
            "Request type '{0}' has no registered IRequestHandler",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateHandler = new DiagnosticDescriptor(
            "ZAM002",
            "Duplicate request handler",
            "Request type '{0}' has multiple handlers: {1}",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ClassRequest = new DiagnosticDescriptor(
            "ZAM003",
            "Request type is a class",
            "Request type '{0}' is a class; use 'readonly record struct' for zero-allocation dispatch",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor InvalidHandlerSignature = new DiagnosticDescriptor(
            "ZAM004",
            "Invalid handler signature",
            "Handler '{0}' has an invalid Handle method signature for IRequestHandler<{1}, {2}>",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingBehaviorHandleMethod = new DiagnosticDescriptor(
            "ZAM005",
            "Missing behavior Handle method",
            "Pipeline behavior '{0}' is missing a public static Handle<TRequest, TResponse> method",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateBehaviorOrder = new DiagnosticDescriptor(
            "ZAM006",
            "Duplicate behavior order",
            "Pipeline behaviors {0} have the same Order value {1}; execution order is ambiguous",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor StreamHandlerWrongReturnType = new DiagnosticDescriptor(
            "ZAM007",
            "Stream handler wrong return type",
            "Stream handler '{0}' Handle method must return IAsyncEnumerable<{1}>",
            "ZeroAlloc.Mediator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
```

**Step 2: Commit**

```bash
git add src/ZeroAlloc.Mediator.Generator/DiagnosticDescriptors.cs
git commit -m "refactor: rename diagnostic IDs ZM00x -> ZAM00x and update category to ZeroAlloc.Mediator"
```

---

### Task 7: Update ZMediatorGenerator.cs — the main generator class

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/ZMediatorGenerator.cs`
- Rename: `src/ZeroAlloc.Mediator.Generator/ZMediatorGenerator.cs` → `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`

This is the largest single change. Make all updates to the file first, then rename it.

**Step 1: Update namespace and class name**

```csharp
// Before
namespace ZMediator.Generator
{
    [Generator]
    public sealed class ZMediatorGenerator : IIncrementalGenerator

// After
namespace ZeroAlloc.Mediator.Generator
{
    [Generator]
    public sealed class MediatorGenerator : IIncrementalGenerator
```

**Step 2: Update the generated source file name**

```csharp
// Before
spc.AddSource("ZMediator.Mediator.g.cs", source);

// After
spc.AddSource("ZeroAlloc.Mediator.g.cs", source);
```

**Step 3: Update all interface name string literals** (used for Roslyn type matching)

```csharp
// Before → After (all occurrences)
"ZMediator.IRequestHandler<TRequest, TResponse>"  →  "ZeroAlloc.IRequestHandler<TRequest, TResponse>"
"ZMediator.INotificationHandler<TNotification>"   →  "ZeroAlloc.INotificationHandler<TNotification>"
"ZMediator.ParallelNotificationAttribute"          →  "ZeroAlloc.ParallelNotificationAttribute"
"ZMediator.INotification"                          →  "ZeroAlloc.INotification"
"ZMediator.IStreamRequestHandler<TRequest, TResponse>" → "ZeroAlloc.IStreamRequestHandler<TRequest, TResponse>"
"ZMediator.PipelineBehaviorAttribute"              →  "ZeroAlloc.PipelineBehaviorAttribute"
"ZMediator.IPipelineBehavior"                      →  "ZeroAlloc.IPipelineBehavior"
"ZMediator.IRequest<TResponse>"                    →  "ZeroAlloc.IRequest<TResponse>"
```

**Step 4: Update the emitted namespace in generated code**

```csharp
// Before
sb.AppendLine("namespace ZMediator");

// After
sb.AppendLine("namespace ZeroAlloc");
```

**Step 5: Update the emitted DI type names**

```csharp
// EmitIZMediatorInterface method — rename the emitted interface
// Before
sb.AppendLine("    public partial interface IZMediator");
// After
sb.AppendLine("    public partial interface IMediator");

// EmitZMediatorService method — rename the emitted class and interface reference
// Before
sb.AppendLine("    public partial class ZMediatorService : IZMediator");
// After
sb.AppendLine("    public partial class MediatorService : IMediator");
```

**Step 6: Rename the private emit methods for clarity**

```csharp
// Rename method signatures (internal refactor, no behavioural change):
EmitIZMediatorInterface  →  EmitIMediatorInterface
EmitZMediatorService     →  EmitMediatorService
```

Update the call sites in `GenerateMediatorClass`:

```csharp
// Before
EmitIZMediatorInterface(sb, validRequests, validNotifications, validStreams);
EmitZMediatorService(sb, validRequests, validNotifications, validStreams);

// After
EmitIMediatorInterface(sb, validRequests, validNotifications, validStreams);
EmitMediatorService(sb, validRequests, validNotifications, validStreams);
```

**Step 7: Rename the file**

```bash
git mv src/ZeroAlloc.Mediator.Generator/ZMediatorGenerator.cs src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs
```

**Step 8: Build the generator to verify**

```bash
dotnet build src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj
```

Expected: 0 errors, 0 warnings.

**Step 9: Commit**

```bash
git add src/ZeroAlloc.Mediator.Generator/
git commit -m "refactor: rename generator class ZMediatorGenerator -> MediatorGenerator, update all ZMediator string literals and emitted type names"
```

---

### Task 8: Update test files

**Files:**
- Modify: `tests/ZeroAlloc.Mediator.Tests/GeneratorTests/GeneratorTestHelper.cs`
- Modify: all other `*.cs` files under `tests/ZeroAlloc.Mediator.Tests/`

**Step 1: Update `GeneratorTestHelper.cs`**

Two changes:
1. Namespace declaration: `namespace ZMediator.Tests.GeneratorTests` → `namespace ZeroAlloc.Mediator.Tests.GeneratorTests`
2. Generator instantiation: `new Generator.ZMediatorGenerator()` → `new Generator.MediatorGenerator()`
3. Generated file filter: `.Contains("ZMediator")` → `.Contains("ZeroAlloc")`

```csharp
namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// ...

var generator = new Generator.MediatorGenerator();

// ...

var generatedTrees = outputCompilation.SyntaxTrees
    .Where(t => t.FilePath.Contains("ZeroAlloc"))
    .ToList();
```

**Step 2: Update namespace declarations in all remaining test files**

For every `*.cs` under `tests/ZeroAlloc.Mediator.Tests/`, change:
- `namespace ZMediator.Tests` → `namespace ZeroAlloc.Mediator.Tests`
- `namespace ZMediator.Tests.GeneratorTests` → `namespace ZeroAlloc.Mediator.Tests.GeneratorTests`
- `namespace ZMediator.Tests.IntegrationTests` → `namespace ZeroAlloc.Mediator.Tests.IntegrationTests`

**Step 3: Update diagnostic ID string literals in test files**

In `DiagnosticTests.cs` and any file asserting diagnostic IDs, replace:
- `"ZM001"` → `"ZAM001"`
- `"ZM002"` → `"ZAM002"`
- `"ZM003"` → `"ZAM003"`
- `"ZM004"` → `"ZAM004"`
- `"ZM005"` → `"ZAM005"`
- `"ZM006"` → `"ZAM006"`
- `"ZM007"` → `"ZAM007"`

**Step 4: Update any test strings referencing `IZMediator` or `ZMediatorService`**

In generator snapshot tests (e.g., `DiInterfaceGeneratorTests.cs`), update expected output strings:
- `"IZMediator"` → `"IMediator"`
- `"ZMediatorService"` → `"MediatorService"`
- `"namespace ZMediator"` → `"namespace ZeroAlloc"`

**Step 5: Update `using ZMediator` statements in integration test files**

```csharp
// Before
using ZMediator;

// After
using ZeroAlloc;
```

**Step 6: Run the tests**

```bash
dotnet test tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj --configuration Release
```

Expected: all tests pass.

**Step 7: Commit**

```bash
git add tests/ZeroAlloc.Mediator.Tests/
git commit -m "refactor: update test namespaces, diagnostic IDs, and generated type name assertions"
```

---

### Task 9: Update sample and benchmark files

**Files:**
- Modify: `samples/ZeroAlloc.Mediator.Sample/Program.cs`
- Modify: `tests/ZeroAlloc.Mediator.Benchmarks/Program.cs`

**Step 1: Update `samples/ZeroAlloc.Mediator.Sample/Program.cs`**

- Change `using ZMediator;` → `using ZeroAlloc;`
- Change `IZMediator` → `IMediator`, `ZMediatorService` → `MediatorService` if referenced
- Change any namespace declaration from `ZMediator.Sample` → `ZeroAlloc.Mediator.Sample`

**Step 2: Update `tests/ZeroAlloc.Mediator.Benchmarks/Program.cs`**

- Change `using ZMediator;` → `using ZeroAlloc;`
- Change `IZMediator` → `IMediator`, `ZMediatorService` → `MediatorService` if referenced
- Change any namespace declaration from `ZMediator.Benchmarks` → `ZeroAlloc.Mediator.Benchmarks`

**Step 3: Do a full solution build**

```bash
dotnet build ZeroAlloc.Mediator.slnx --configuration Release
```

Expected: 0 errors.

**Step 4: Run all tests**

```bash
dotnet test ZeroAlloc.Mediator.slnx --configuration Release
```

Expected: all tests pass.

**Step 5: Commit**

```bash
git add samples/ tests/ZeroAlloc.Mediator.Benchmarks/
git commit -m "refactor: update sample and benchmark namespaces and using directives"
```

---

### Task 10: Update CI/CD workflows

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Step 1: Update `ci.yml`** — replace all `ZMediator.slnx` references and benchmark path

```yaml
# Restore
run: dotnet restore ZeroAlloc.Mediator.slnx

# Build
run: dotnet build ZeroAlloc.Mediator.slnx --configuration Release --no-restore

# Test
run: dotnet test ZeroAlloc.Mediator.slnx --configuration Release --no-build --verbosity normal --logger "trx;LogFileName=test-results.trx"

# Benchmark run
run: dotnet run --project tests/ZeroAlloc.Mediator.Benchmarks --configuration Release -- --filter "*" --exporters json
```

**Step 2: Update `release.yml`** — replace solution reference and pack step project paths

```yaml
# Restore
run: dotnet restore ZeroAlloc.Mediator.slnx

# Build
run: dotnet build ZeroAlloc.Mediator.slnx --configuration Release --no-restore -p:Version=${{ needs.release-please.outputs.version }}

# Test
run: dotnet test ZeroAlloc.Mediator.slnx --configuration Release --no-build

# Pack steps
- name: Pack ZeroAlloc.Mediator
  run: dotnet pack src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj --configuration Release --no-build -p:Version=${{ needs.release-please.outputs.version }} -o ./artifacts

- name: Pack ZeroAlloc.Mediator.Generator
  run: dotnet pack src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj --configuration Release --no-build -p:Version=${{ needs.release-please.outputs.version }} -o ./artifacts
```

**Step 3: Commit**

```bash
git add .github/
git commit -m "ci: update workflows for ZeroAlloc.Mediator rename"
```

---

### Task 11: Update README

**Files:**
- Modify: `README.md`

**Step 1: Update the title and intro**

```markdown
# ZeroAlloc.Mediator

A zero-allocation mediator library for .NET. Uses a Roslyn incremental source generator...
```

**Step 2: Update NuGet package references in Quick Start**

```xml
<PackageReference Include="ZeroAlloc.Mediator" Version="0.1.0" />
<PackageReference Include="ZeroAlloc.Mediator.Generator" Version="0.1.0" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

**Step 3: Update `using` directives in all code examples**

```csharp
using ZeroAlloc;
```

**Step 4: Update DI section**

```csharp
// Registration
services.AddSingleton<IMediator, MediatorService>();

// Injection
public class OrderController(IMediator mediator)
```

**Step 5: Update Analyzer Diagnostics table**

| ID | Severity | Description |
|---|---|---|
| ZAM001 | Error | Request type has no registered handler |
| ZAM002 | Error | Request type has multiple handlers |
| ZAM003 | Warning | Request type is a class — use `readonly record struct` |
| ZAM004 | Error | Handler method signature doesn't match expected pattern |
| ZAM005 | Error | Pipeline behavior missing static `Handle<TRequest, TResponse>` method |
| ZAM006 | Warning | Duplicate `[PipelineBehavior(Order)]` values — ambiguous ordering |
| ZAM007 | Error | Stream handler returns wrong type instead of `IAsyncEnumerable` |

**Step 6: Update Project Structure tree**

```
ZeroAlloc.Mediator/
├── src/
│   ├── ZeroAlloc.Mediator/               # Core abstractions
│   └── ZeroAlloc.Mediator.Generator/     # Source generator
├── tests/
│   ├── ZeroAlloc.Mediator.Tests/
│   └── ZeroAlloc.Mediator.Benchmarks/
└── samples/
    └── ZeroAlloc.Mediator.Sample/
```

**Step 7: Update benchmark table header** (replace `ZMediator_` prefix with `ZeroAlloc_`)

**Step 8: Commit**

```bash
git add README.md
git commit -m "docs: update README for ZeroAlloc.Mediator rebrand"
```

---

### Task 12: Final verification

**Step 1: Full clean build**

```bash
dotnet build ZeroAlloc.Mediator.slnx --configuration Release
```

Expected: 0 errors, 0 warnings.

**Step 2: Run all tests**

```bash
dotnet test ZeroAlloc.Mediator.slnx --configuration Release
```

Expected: all tests pass.

**Step 3: Verify no old names remain in source files**

```bash
grep -r "ZMediator" src/ tests/ samples/ --include="*.cs" --include="*.csproj" --include="*.slnx"
```

Expected: no output (zero matches).

**Step 4: Final commit if any stragglers found, then push**

```bash
git push
```

---

### Manual GitHub Steps (after PR merges to main)

Do these in order on GitHub:

1. Create organisation `ZeroAlloc-NET`
2. Go to repo Settings → Transfer ownership → `ZeroAlloc-NET/ZeroAlloc.Mediator`
3. Update your local remote:
   ```bash
   git remote set-url origin https://github.com/ZeroAlloc-NET/ZeroAlloc.Mediator
   ```
