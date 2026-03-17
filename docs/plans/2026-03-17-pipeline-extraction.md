# ZeroAlloc.Pipeline Extraction Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extract the pipeline behavior concept from `ZeroAlloc.Mediator` into a standalone `ZeroAlloc.Pipeline` solution at `C:\Projects\Prive\ZeroAlloc.Pipeline`, then migrate `ZeroAlloc.Mediator` to consume it — with zero breaking changes for existing consumers.

**Architecture:** Two new NuGet packages: `ZeroAlloc.Pipeline` (runtime marker types) and `ZeroAlloc.Pipeline.Generators` (plain netstandard2.0 DLL with shared Roslyn discovery + emission helpers). `ZeroAlloc.Mediator` keeps its own `IPipelineBehavior` and `PipelineBehaviorAttribute` in the `ZeroAlloc.Mediator` namespace as thin subclasses — existing consumer code compiles unchanged. The `PipelineEmitter` is parameterized by `PipelineShape` so any framework (mediator, validation, etc.) can inline its own delegate signature.

**Tech Stack:** C# 13, .NET 10 / netstandard2.0, Roslyn Incremental Source Generators (`Microsoft.CodeAnalysis.CSharp 4.12.0`), xUnit 2.x, `.slnx` solution format, MSBuild `Directory.Build.props`.

---

## Context: How the Current Pipeline Works in ZeroAlloc.Mediator

The pipeline is entirely compile-time. The generator discovers classes decorated with `[PipelineBehavior]` that implement `IPipelineBehavior`, sorts them by `Order`, and inlines nested static lambda calls around each `Send` method. No runtime allocation. Example output for two behaviors:

```csharp
public static ValueTask<string> Send(global::App.Ping request, CancellationToken ct = default)
{
    return LoggingBehavior.Handle<global::App.Ping, string>(
        request, ct, static (r1, c1) =>
        ValidationBehavior.Handle<global::App.Ping, string>(
            r1, c1, static (r2, c2) =>
            { var handler = _pingHandlerFactory?.Invoke() ?? new PingHandler(); return handler.Handle(r2, c2); }));
}
```

The `PipelineShape` abstraction in the new package generalizes this pattern: different delegate shapes (2 type params + `ct` for mediator; 1 type param, no `ct` for validation) all produce equivalent nested static lambdas.

---

## Phase 1 — New ZeroAlloc.Pipeline Solution

### Task 1: Scaffold the solution

**Files:**
- Create: `C:\Projects\Prive\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.slnx`
- Create: `C:\Projects\Prive\ZeroAlloc.Pipeline\Directory.Build.props`
- Create: `C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.csproj`
- Create: `C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline.Generators\ZeroAlloc.Pipeline.Generators.csproj`
- Create: `C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests\ZeroAlloc.Pipeline.Generators.Tests.csproj`

**Step 1: Initialize git repo**

```bash
cd C:\Projects\Prive\ZeroAlloc.Pipeline
git init
```

**Step 2: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Authors>Marcel Roozekrans</Authors>
    <Company>Marcel Roozekrans</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.Pipeline</PackageProjectUrl>
    <RepositoryUrl>https://github.com/ZeroAlloc-Net/ZeroAlloc.Pipeline</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <Copyright>Copyright (c) Marcel Roozekrans</Copyright>
  </PropertyGroup>
</Project>
```

**Step 3: Create `ZeroAlloc.Pipeline.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ZeroAlloc.Pipeline/ZeroAlloc.Pipeline.csproj" />
    <Project Path="src/ZeroAlloc.Pipeline.Generators/ZeroAlloc.Pipeline.Generators.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ZeroAlloc.Pipeline.Generators.Tests/ZeroAlloc.Pipeline.Generators.Tests.csproj" />
  </Folder>
</Solution>
```

**Step 4: Create runtime `.csproj`**

`src/ZeroAlloc.Pipeline/ZeroAlloc.Pipeline.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <PackageId>ZeroAlloc.Pipeline</PackageId>
    <IsAotCompatible>true</IsAotCompatible>
    <Description>Shared pipeline behavior contracts for ZeroAlloc source-generated libraries.</Description>
    <PackageTags>pipeline;source-generator;zero-allocation;roslyn</PackageTags>
  </PropertyGroup>
</Project>
```

**Step 5: Create generators helper `.csproj`**

`src/ZeroAlloc.Pipeline.Generators/ZeroAlloc.Pipeline.Generators.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>ZeroAlloc.Pipeline.Generators</PackageId>
    <Description>Roslyn helper library for generator authors — pipeline behavior discovery and emission.</Description>
    <PackageTags>pipeline;source-generator;roslyn</PackageTags>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.csproj" />
  </ItemGroup>
</Project>
```

**Step 6: Create tests `.csproj`**

`tests/ZeroAlloc.Pipeline.Generators.Tests/ZeroAlloc.Pipeline.Generators.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.csproj" />
    <ProjectReference Include="..\..\src\ZeroAlloc.Pipeline.Generators\ZeroAlloc.Pipeline.Generators.csproj" />
  </ItemGroup>
</Project>
```

**Step 7: Verify solution builds**

```bash
dotnet build C:\Projects\Prive\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.slnx
```
Expected: Build succeeded (empty projects, no sources yet).

**Step 8: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "chore: scaffold ZeroAlloc.Pipeline solution"
```

---

### Task 2: Runtime package — `IPipelineBehavior` and `PipelineBehaviorAttribute`

**Files:**
- Create: `src/ZeroAlloc.Pipeline/IPipelineBehavior.cs`
- Create: `src/ZeroAlloc.Pipeline/PipelineBehaviorAttribute.cs`

**Step 1: Create `IPipelineBehavior.cs`**

```csharp
namespace ZeroAlloc.Pipeline;

/// <summary>
/// Marker interface for all pipeline behavior classes.
/// Implement this (or a framework-specific sub-interface) and decorate with
/// <see cref="PipelineBehaviorAttribute"/> to participate in the generated pipeline.
/// </summary>
public interface IPipelineBehavior;
```

**Step 2: Create `PipelineBehaviorAttribute.cs`**

```csharp
namespace ZeroAlloc.Pipeline;

/// <summary>
/// Marks a class as a pipeline behavior and controls its position in the chain.
/// The class must also implement <see cref="IPipelineBehavior"/> and expose a
/// public static <c>Handle</c> method matching the host framework's delegate shape.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class PipelineBehaviorAttribute(int order = 0) : Attribute
{
    /// <summary>Execution order. Lower values run first (outermost).</summary>
    public int Order { get; set; } = order;

    /// <summary>
    /// When set, this behavior only applies to the specified request/model type.
    /// When null, the behavior applies to all types in the pipeline.
    /// </summary>
    public Type? AppliesTo { get; set; }
}
```

**Step 3: Build**

```bash
dotnet build C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.csproj
```
Expected: Build succeeded.

**Step 4: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "feat: add IPipelineBehavior and PipelineBehaviorAttribute"
```

---

### Task 3: `PipelineBehaviorInfo` data class

This is the data transfer object used between the discoverer and the emitter. Same structure as the current `ZeroAlloc.Mediator.Generator.PipelineBehaviorInfo` but adds `HandleMethodTypeParameterCount` so each framework can validate the expected type parameter count.

**Files:**
- Create: `src/ZeroAlloc.Pipeline.Generators/PipelineBehaviorInfo.cs`
- Create: `tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineBehaviorInfoTests.cs`

**Step 1: Write the failing test**

`tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineBehaviorInfoTests.cs`:
```csharp
namespace ZeroAlloc.Pipeline.Generators.Tests;

public class PipelineBehaviorInfoTests
{
    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new PipelineBehaviorInfo("global::App.Foo", 1, "global::App.Bar", 2);
        var b = new PipelineBehaviorInfo("global::App.Foo", 1, "global::App.Bar", 2);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentOrder_NotEqual()
    {
        var a = new PipelineBehaviorInfo("global::App.Foo", 1, null, 2);
        var b = new PipelineBehaviorInfo("global::App.Foo", 2, null, 2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HasValidHandleMethod_WhenCountMatchesExpected_ReturnsTrue()
    {
        var info = new PipelineBehaviorInfo("global::App.Foo", 0, null, typeParamCount: 2);
        Assert.True(info.HasValidHandleMethod(expectedTypeParamCount: 2));
        Assert.False(info.HasValidHandleMethod(expectedTypeParamCount: 1));
    }

    [Fact]
    public void HasValidHandleMethod_WhenNoHandleMethod_ReturnsFalse()
    {
        var info = new PipelineBehaviorInfo("global::App.Foo", 0, null, typeParamCount: -1);
        Assert.False(info.HasValidHandleMethod(expectedTypeParamCount: 2));
    }
}
```

**Step 2: Run test — verify it fails**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests --no-build 2>&1 | head -20
```
Expected: FAIL — `PipelineBehaviorInfo` not found.

**Step 3: Create `PipelineBehaviorInfo.cs`**

`src/ZeroAlloc.Pipeline.Generators/PipelineBehaviorInfo.cs`:
```csharp
#nullable enable
namespace ZeroAlloc.Pipeline.Generators;

public sealed class PipelineBehaviorInfo : IEquatable<PipelineBehaviorInfo>
{
    /// <summary>Fully qualified type name, e.g. "global::App.LoggingBehavior".</summary>
    public string BehaviorTypeName { get; }

    public int Order { get; }

    /// <summary>Fully qualified type name this behavior is scoped to, or null for all types.</summary>
    public string? AppliesTo { get; }

    /// <summary>
    /// Number of type parameters on the public static Handle method found.
    /// -1 means no Handle method was found at all.
    /// </summary>
    public int HandleMethodTypeParameterCount { get; }

    public PipelineBehaviorInfo(
        string behaviorTypeName,
        int order,
        string? appliesTo,
        int typeParamCount)
    {
        BehaviorTypeName = behaviorTypeName;
        Order = order;
        AppliesTo = appliesTo;
        HandleMethodTypeParameterCount = typeParamCount;
    }

    /// <summary>Returns true when a Handle method exists with the expected number of type parameters.</summary>
    public bool HasValidHandleMethod(int expectedTypeParamCount)
        => HandleMethodTypeParameterCount == expectedTypeParamCount;

    public bool Equals(PipelineBehaviorInfo? other)
    {
        if (other is null) return false;
        return BehaviorTypeName == other.BehaviorTypeName
            && Order == other.Order
            && AppliesTo == other.AppliesTo
            && HandleMethodTypeParameterCount == other.HandleMethodTypeParameterCount;
    }

    public override bool Equals(object? obj) => Equals(obj as PipelineBehaviorInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + BehaviorTypeName.GetHashCode();
            hash = hash * 31 + Order.GetHashCode();
            hash = hash * 31 + (AppliesTo?.GetHashCode() ?? 0);
            hash = hash * 31 + HandleMethodTypeParameterCount.GetHashCode();
            return hash;
        }
    }
}
```

**Step 4: Run tests — verify they pass**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: 4 passed.

**Step 5: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "feat: add PipelineBehaviorInfo data class"
```

---

### Task 4: `PipelineBehaviorDiscoverer`

Discovers classes in a Roslyn compilation that have `[PipelineBehavior]` (or any subclass of it) and implement `IPipelineBehavior` (or any sub-interface). Works for both direct `ZeroAlloc.Pipeline` types and framework-specific subclasses (e.g. `ZeroAlloc.Mediator.IPipelineBehavior`).

**Files:**
- Create: `src/ZeroAlloc.Pipeline.Generators/PipelineBehaviorDiscoverer.cs`
- Create: `tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineBehaviorDiscovererTests.cs`

**Step 1: Write the failing tests**

`tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineBehaviorDiscovererTests.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Pipeline.Generators.Tests;

public class PipelineBehaviorDiscovererTests
{
    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IPipelineBehavior).Assembly.Location),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void Discover_BehaviorWithAttribute_ReturnsInfo()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            using System.Threading;
            using System.Threading.Tasks;

            [PipelineBehavior(Order = 1)]
            public class MyBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    System.Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : class
                    => next(request, ct);
            }
            """;

        var compilation = CreateCompilation(source);
        var results = PipelineBehaviorDiscoverer.Discover(compilation).ToList();

        Assert.Single(results);
        Assert.Equal(1, results[0].Order);
        Assert.Equal(2, results[0].HandleMethodTypeParameterCount);
        Assert.Null(results[0].AppliesTo);
    }

    [Fact]
    public void Discover_BehaviorWithAppliesTo_SetsAppliesTo()
    {
        var source = """
            using ZeroAlloc.Pipeline;

            public class MyModel { }

            [PipelineBehavior(AppliesTo = typeof(MyModel))]
            public class ScopedBehavior : IPipelineBehavior
            {
                public static string Handle<T>(T instance, System.Func<T, string> next) => next(instance);
            }
            """;

        var compilation = CreateCompilation(source);
        var results = PipelineBehaviorDiscoverer.Discover(compilation).ToList();

        Assert.Single(results);
        Assert.NotNull(results[0].AppliesTo);
        Assert.Contains("MyModel", results[0].AppliesTo);
    }

    [Fact]
    public void Discover_ClassWithoutAttribute_IsIgnored()
    {
        var source = """
            using ZeroAlloc.Pipeline;
            public class NotABehavior : IPipelineBehavior { }
            """;

        var compilation = CreateCompilation(source);
        var results = PipelineBehaviorDiscoverer.Discover(compilation).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void Discover_BehaviorWithoutHandleMethod_ReturnsNegativeTypeParamCount()
    {
        var source = """
            using ZeroAlloc.Pipeline;

            [PipelineBehavior]
            public class NoHandleBehavior : IPipelineBehavior { }
            """;

        var compilation = CreateCompilation(source);
        var results = PipelineBehaviorDiscoverer.Discover(compilation).ToList();

        Assert.Single(results);
        Assert.Equal(-1, results[0].HandleMethodTypeParameterCount);
    }

    [Fact]
    public void Discover_SubclassedAttribute_IsDetected()
    {
        // Simulates ZeroAlloc.Mediator.PipelineBehaviorAttribute : ZeroAlloc.Pipeline.PipelineBehaviorAttribute
        var source = """
            using ZeroAlloc.Pipeline;

            public sealed class MediatorPipelineBehaviorAttribute : PipelineBehaviorAttribute
            {
                public MediatorPipelineBehaviorAttribute(int order = 0) : base(order) { }
            }

            public interface IMediatorBehavior : IPipelineBehavior { }

            [MediatorPipelineBehavior(Order = 2)]
            public class MyBehavior : IMediatorBehavior
            {
                public static string Handle<T>(T r, System.Func<T, string> next) => next(r);
            }
            """;

        var compilation = CreateCompilation(source);
        var results = PipelineBehaviorDiscoverer.Discover(compilation).ToList();

        Assert.Single(results);
        Assert.Equal(2, results[0].Order);
    }
}
```

**Step 2: Run — verify it fails**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: FAIL — `PipelineBehaviorDiscoverer` not found.

**Step 3: Create `PipelineBehaviorDiscoverer.cs`**

`src/ZeroAlloc.Pipeline.Generators/PipelineBehaviorDiscoverer.cs`:
```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Pipeline.Generators;

public static class PipelineBehaviorDiscoverer
{
    private const string PipelineBehaviorAttributeFqn = "ZeroAlloc.Pipeline.PipelineBehaviorAttribute";
    private const string IPipelineBehaviorFqn = "ZeroAlloc.Pipeline.IPipelineBehavior";

    /// <summary>
    /// Discovers all pipeline behaviors in <paramref name="compilation"/>.
    /// Detects both direct <c>ZeroAlloc.Pipeline.PipelineBehaviorAttribute</c> usage and
    /// any subclasses of it (e.g. <c>ZeroAlloc.Mediator.PipelineBehaviorAttribute</c>).
    /// </summary>
    public static IEnumerable<PipelineBehaviorInfo> Discover(Compilation compilation)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var classDeclarations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => c.AttributeLists.Count > 0);

            foreach (var classDecl in classDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
                if (symbol == null) continue;

                var info = TryGetBehaviorInfo(symbol);
                if (info != null)
                    yield return info;
            }
        }
    }

    private static PipelineBehaviorInfo? TryGetBehaviorInfo(INamedTypeSymbol symbol)
    {
        // Must have an attribute that is or derives from PipelineBehaviorAttribute
        AttributeData? pipelineAttr = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass != null && InheritsFrom(attr.AttributeClass, PipelineBehaviorAttributeFqn))
            {
                pipelineAttr = attr;
                break;
            }
        }
        if (pipelineAttr == null) return null;

        // Must implement IPipelineBehavior (or a sub-interface)
        var implementsPipeline = symbol.AllInterfaces.Any(i => InheritsFrom(i, IPipelineBehaviorFqn));
        if (!implementsPipeline) return null;

        var behaviorTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var order = ReadOrder(pipelineAttr);
        var appliesTo = ReadAppliesTo(pipelineAttr);
        var typeParamCount = GetHandleMethodTypeParamCount(symbol);

        return new PipelineBehaviorInfo(behaviorTypeName, order, appliesTo, typeParamCount);
    }

    private static bool InheritsFrom(INamedTypeSymbol symbol, string fullName)
    {
        var current = symbol;
        while (current != null)
        {
            if (current.ToDisplayString() == fullName) return true;
            // Also check all interfaces (for interface hierarchies)
            if (current.AllInterfaces.Any(i => i.ToDisplayString() == fullName)) return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool InheritsFrom(ITypeSymbol symbol, string fullName)
    {
        if (symbol.ToDisplayString() == fullName) return true;
        var named = symbol as INamedTypeSymbol;
        if (named != null) return InheritsFrom(named, fullName);
        return false;
    }

    private static int ReadOrder(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length > 0
            && attr.ConstructorArguments[0].Value is int ctorOrder)
            return ctorOrder;

        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "Order" && named.Value.Value is int namedOrder)
                return namedOrder;
        }
        return 0;
    }

    private static string? ReadAppliesTo(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "AppliesTo" && named.Value.Value is INamedTypeSymbol typeSymbol)
                return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        return null;
    }

    private static int GetHandleMethodTypeParamCount(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers())
        {
            if (member is IMethodSymbol method
                && method.Name == "Handle"
                && method.IsStatic
                && method.DeclaredAccessibility == Accessibility.Public
                && method.TypeParameters.Length > 0)
            {
                return method.TypeParameters.Length;
            }
        }
        return -1;
    }
}
```

**Step 4: Run tests — verify they pass**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: All 9 tests pass.

**Step 5: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "feat: add PipelineBehaviorDiscoverer"
```

---

### Task 5: `PipelineShape` and `PipelineEmitter`

The emitter takes a list of `PipelineBehaviorInfo` (sorted by order, pre-filtered to only those applicable to the current type) and a `PipelineShape` that describes the delegate signature. It returns a C# expression string ready to embed in a generated method body.

**Files:**
- Create: `src/ZeroAlloc.Pipeline.Generators/PipelineShape.cs`
- Create: `src/ZeroAlloc.Pipeline.Generators/PipelineEmitter.cs`
- Create: `tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineEmitterTests.cs`

**Step 1: Write the failing tests**

`tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineEmitterTests.cs`:
```csharp
namespace ZeroAlloc.Pipeline.Generators.Tests;

public class PipelineEmitterTests
{
    // Shape that matches ZeroAlloc.Mediator's Send(request, ct) pattern
    private static PipelineShape MediatorShape(string requestType, string responseType, string innermostBody)
        => new PipelineShape
        {
            TypeArguments = [requestType, responseType],
            OuterParameterNames = ["request", "ct"],
            LambdaParameterPrefixes = ["r", "c"],
            InnermostBodyTemplate = innermostBody,
        };

    // Shape that matches ZeroAlloc.Validation's Validate(instance) pattern
    private static PipelineShape ValidationShape(string modelType, string innermostBody)
        => new PipelineShape
        {
            TypeArguments = [modelType],
            OuterParameterNames = ["instance"],
            LambdaParameterPrefixes = ["r"],
            InnermostBodyTemplate = innermostBody,
        };

    [Fact]
    public void EmitChain_NoBehaviors_ReturnsInnermostBody()
    {
        var shape = MediatorShape("global::App.Ping", "string", "handler.Handle(request, ct)");
        var result = PipelineEmitter.EmitChain([], shape);
        Assert.Equal("handler.Handle(request, ct)", result);
    }

    [Fact]
    public void EmitChain_OneBehavior_WrapsInnermost()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.LoggingBehavior", 0, null, 2)
        };
        var shape = MediatorShape(
            "global::App.Ping", "string",
            "{ var h = new PingHandler(); return h.Handle(r1, c1); }");

        var result = PipelineEmitter.EmitChain(behaviors, shape);

        Assert.Contains("LoggingBehavior.Handle<global::App.Ping, string>", result);
        Assert.Contains("request, ct", result);
        Assert.Contains("static (r1, c1)", result);
    }

    [Fact]
    public void EmitChain_TwoBehaviors_NestedCorrectly()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.LoggingBehavior", 0, null, 2),
            new PipelineBehaviorInfo("global::App.ValidationBehavior", 1, null, 2),
        };
        var shape = MediatorShape(
            "global::App.Ping", "string",
            "{ var h = new PingHandler(); return h.Handle(r2, c2); }");

        var result = PipelineEmitter.EmitChain(behaviors, shape);

        // Outer uses original params
        Assert.Contains("LoggingBehavior.Handle<global::App.Ping, string>(\n                request, ct,", result);
        // Inner uses lambda params
        Assert.Contains("ValidationBehavior.Handle<global::App.Ping, string>", result);
        Assert.Contains("r1, c1,", result);
        Assert.Contains("static (r2, c2)", result);
    }

    [Fact]
    public void EmitChain_ValidationShape_SingleTypeArg()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.CachingBehavior", 0, null, 1)
        };
        var shape = ValidationShape(
            "global::App.Order",
            "{ return new OrderValidator().Validate(r1); }");

        var result = PipelineEmitter.EmitChain(behaviors, shape);

        Assert.Contains("CachingBehavior.Handle<global::App.Order>", result);
        Assert.Contains("instance,", result);
        Assert.Contains("static (r1)", result);
    }
}
```

**Step 2: Run — verify it fails**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: FAIL — `PipelineShape` and `PipelineEmitter` not found.

**Step 3: Create `PipelineShape.cs`**

`src/ZeroAlloc.Pipeline.Generators/PipelineShape.cs`:
```csharp
#nullable enable
namespace ZeroAlloc.Pipeline.Generators;

/// <summary>
/// Describes the delegate shape of a pipeline so <see cref="PipelineEmitter"/>
/// can generate the correct nested static lambda call chain.
/// </summary>
public sealed class PipelineShape
{
    /// <summary>
    /// Concrete type arguments for <c>Handle&lt;...&gt;</c>.
    /// ZMediator: ["global::App.Ping", "string"].
    /// ZValidation: ["global::App.Order"].
    /// </summary>
    public required string[] TypeArguments { get; init; }

    /// <summary>
    /// Parameter names at the outermost call site.
    /// ZMediator: ["request", "ct"].
    /// ZValidation: ["instance"].
    /// </summary>
    public required string[] OuterParameterNames { get; init; }

    /// <summary>
    /// One prefix letter per outer parameter, used to name lambda params at each nesting level.
    /// Level N produces "{prefix}{N}" for each prefix.
    /// ZMediator: ["r", "c"] → r1,c1  r2,c2 …
    /// ZValidation: ["r"] → r1  r2 …
    /// </summary>
    public required string[] LambdaParameterPrefixes { get; init; }

    /// <summary>
    /// The body of the innermost (non-behavior) call.
    /// Use the lambda param names as they appear at the deepest nesting level.
    /// Example (ZMediator, 2 behaviors): "{ var h = factory?.Invoke() ?? new Handler(); return h.Handle(r2, c2); }"
    /// </summary>
    public required string InnermostBodyTemplate { get; init; }
}
```

**Step 4: Create `PipelineEmitter.cs`**

`src/ZeroAlloc.Pipeline.Generators/PipelineEmitter.cs`:
```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroAlloc.Pipeline.Generators;

public static class PipelineEmitter
{
    /// <summary>
    /// Emits a nested static lambda call chain for the given behaviors and shape.
    /// </summary>
    /// <param name="behaviors">
    /// Behaviors to chain, pre-filtered (AppliesTo already checked) and sorted by Order ascending.
    /// </param>
    /// <param name="shape">Delegate shape describing type args, parameter names, and the innermost body.</param>
    /// <returns>A C# expression string ready to be placed after <c>return </c> in a generated method.</returns>
    public static string EmitChain(
        IReadOnlyList<PipelineBehaviorInfo> behaviors,
        PipelineShape shape)
    {
        if (behaviors.Count == 0)
            return shape.InnermostBodyTemplate;

        var typeArgs = "<" + string.Join(", ", shape.TypeArguments) + ">";
        var depth = behaviors.Count;

        // Build innermost lambda: static (r{depth}, c{depth}) => { ... }
        var lambdaParams = BuildLambdaParams(shape.LambdaParameterPrefixes, depth);
        var innermost = $"static {lambdaParams} =>\n                    {shape.InnermostBodyTemplate}";

        var result = innermost;

        for (var i = depth - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            if (i == 0)
            {
                // Outermost: use the real parameter names
                var outerParams = string.Join(", ", shape.OuterParameterNames);
                result = $"{behavior.BehaviorTypeName}.Handle{typeArgs}(\n                {outerParams}, {result})";
            }
            else
            {
                // Intermediate: wrap in a lambda using level-i param names
                var levelParams = BuildLambdaParams(shape.LambdaParameterPrefixes, i);
                var levelParamRefs = BuildParamRefs(shape.LambdaParameterPrefixes, i);
                result = $"static {levelParams} =>\n                {behavior.BehaviorTypeName}.Handle{typeArgs}(\n                    {levelParamRefs}, {result})";
            }
        }

        return result;
    }

    private static string BuildLambdaParams(string[] prefixes, int level)
    {
        if (prefixes.Length == 1)
            return $"({prefixes[0]}{level})";

        var parts = prefixes.Select(p => $"{p}{level}");
        return "(" + string.Join(", ", parts) + ")";
    }

    private static string BuildParamRefs(string[] prefixes, int level)
    {
        var parts = prefixes.Select(p => $"{p}{level}");
        return string.Join(", ", parts);
    }
}
```

**Step 5: Run tests — verify they pass**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: All 13 tests pass.

**Step 6: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "feat: add PipelineShape and PipelineEmitter"
```

---

### Task 6: `PipelineDiagnosticRules`

Provides reusable detection logic for the two universal pipeline diagnostics: missing `Handle` method and duplicate `Order`. Each consuming generator maps these to their own diagnostic IDs and error messages.

**Files:**
- Create: `src/ZeroAlloc.Pipeline.Generators/PipelineDiagnosticRules.cs`
- Create: `tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineDiagnosticRulesTests.cs`

**Step 1: Write the failing tests**

`tests/ZeroAlloc.Pipeline.Generators.Tests/PipelineDiagnosticRulesTests.cs`:
```csharp
namespace ZeroAlloc.Pipeline.Generators.Tests;

public class PipelineDiagnosticRulesTests
{
    [Fact]
    public void FindMissingHandleMethod_ReturnsOnlyInvalid()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.Good", 0, null, typeParamCount: 2),
            new PipelineBehaviorInfo("global::App.Bad", 1, null, typeParamCount: -1),
        };

        var invalid = PipelineDiagnosticRules.FindMissingHandleMethod(behaviors, expectedTypeParamCount: 2).ToList();

        Assert.Single(invalid);
        Assert.Equal("global::App.Bad", invalid[0].BehaviorTypeName);
    }

    [Fact]
    public void FindDuplicateOrders_ReturnsDuplicateGroups()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.A", 1, null, 2),
            new PipelineBehaviorInfo("global::App.B", 1, null, 2),
            new PipelineBehaviorInfo("global::App.C", 2, null, 2),
        };

        var duplicates = PipelineDiagnosticRules.FindDuplicateOrders(behaviors).ToList();

        Assert.Single(duplicates);
        Assert.Equal(2, duplicates[0].Count());
    }

    [Fact]
    public void FindDuplicateOrders_NoDuplicates_ReturnsEmpty()
    {
        var behaviors = new[]
        {
            new PipelineBehaviorInfo("global::App.A", 0, null, 2),
            new PipelineBehaviorInfo("global::App.B", 1, null, 2),
        };

        var duplicates = PipelineDiagnosticRules.FindDuplicateOrders(behaviors).ToList();

        Assert.Empty(duplicates);
    }
}
```

**Step 2: Run — verify it fails**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: FAIL — `PipelineDiagnosticRules` not found.

**Step 3: Create `PipelineDiagnosticRules.cs`**

`src/ZeroAlloc.Pipeline.Generators/PipelineDiagnosticRules.cs`:
```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace ZeroAlloc.Pipeline.Generators;

public static class PipelineDiagnosticRules
{
    /// <summary>
    /// Returns behaviors that do not have a valid <c>Handle</c> method
    /// with the expected number of type parameters.
    /// Map these to your own diagnostic ID (e.g. ZAM005, ZV005).
    /// </summary>
    public static IEnumerable<PipelineBehaviorInfo> FindMissingHandleMethod(
        IEnumerable<PipelineBehaviorInfo> behaviors,
        int expectedTypeParamCount)
        => behaviors.Where(b => !b.HasValidHandleMethod(expectedTypeParamCount));

    /// <summary>
    /// Returns groups of behaviors that share the same <see cref="PipelineBehaviorInfo.Order"/> value.
    /// Only groups with more than one entry are returned.
    /// Map these to your own diagnostic ID (e.g. ZAM006, ZV006).
    /// </summary>
    public static IEnumerable<IGrouping<int, PipelineBehaviorInfo>> FindDuplicateOrders(
        IEnumerable<PipelineBehaviorInfo> behaviors)
        => behaviors
            .GroupBy(b => b.Order)
            .Where(g => g.Count() > 1);
}
```

**Step 4: Run tests — verify all pass**

```bash
dotnet test C:\Projects\Prive\ZeroAlloc.Pipeline\tests\ZeroAlloc.Pipeline.Generators.Tests -v minimal
```
Expected: All 16 tests pass.

**Step 5: Commit**

```bash
git -C C:\Projects\Prive\ZeroAlloc.Pipeline add .
git -C C:\Projects\Prive\ZeroAlloc.Pipeline commit -m "feat: add PipelineDiagnosticRules"
```

---

## Phase 2 — Migrate ZeroAlloc.Mediator (Non-Breaking)

Work in the existing `C:\Projects\Prive\ZMediator` repo. Create a new branch first.

### Task 7: Add `ZeroAlloc.Pipeline` dependency and update runtime shims

**Files:**
- Modify: `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj`
- Modify: `src/ZeroAlloc.Mediator/IPipelineBehavior.cs`
- Modify: `src/ZeroAlloc.Mediator/PipelineBehaviorAttribute.cs`

**Step 1: Create migration branch**

```bash
git -C C:\Projects\Prive\ZMediator checkout -b feat/use-zerolloc-pipeline
```

**Step 2: Add project reference (use ProjectReference during development)**

`src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj` — add:
```xml
<ItemGroup>
  <ProjectReference Include="C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline\ZeroAlloc.Pipeline.csproj" />
</ItemGroup>
```

> **Note:** When publishing NuGet, replace this with `<PackageReference Include="ZeroAlloc.Pipeline" Version="x.x.x" />`.

**Step 3: Update `IPipelineBehavior.cs` — extend the shared type**

Replace the entire file:
```csharp
namespace ZeroAlloc.Mediator;

/// <summary>
/// Marker interface for ZeroAlloc.Mediator pipeline behaviors.
/// Extend this (or use <see cref="ZeroAlloc.Pipeline.IPipelineBehavior"/> directly)
/// and decorate with <see cref="PipelineBehaviorAttribute"/>.
/// </summary>
public interface IPipelineBehavior : ZeroAlloc.Pipeline.IPipelineBehavior;
```

**Step 4: Update `PipelineBehaviorAttribute.cs` — extend the shared type**

Replace the entire file:
```csharp
namespace ZeroAlloc.Mediator;

/// <summary>
/// Marks a class as a ZeroAlloc.Mediator pipeline behavior.
/// Identical API to <see cref="ZeroAlloc.Pipeline.PipelineBehaviorAttribute"/> —
/// kept here for backward compatibility so existing consumers need no code changes.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PipelineBehaviorAttribute(int order = 0)
    : ZeroAlloc.Pipeline.PipelineBehaviorAttribute(order);
```

**Step 5: Build and run tests**

```bash
dotnet build C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx
dotnet test C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -v minimal
```
Expected: All existing tests pass — no consumer code changes needed.

**Step 6: Commit**

```bash
git -C C:\Projects\Prive\ZMediator add src/ZeroAlloc.Mediator/
git -C C:\Projects\Prive\ZMediator commit -m "feat: extend IPipelineBehavior and PipelineBehaviorAttribute from ZeroAlloc.Pipeline"
```

---

### Task 8: Migrate the generator — replace discovery with shared discoverer

The generator currently does its own pipeline discovery in `GetPipelineBehaviorInfo()`. Replace it with `PipelineBehaviorDiscoverer.Discover()`.

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj`
- Modify: `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`
- Delete: `src/ZeroAlloc.Mediator.Generator/PipelineBehaviorInfo.cs`

**Step 1: Add project reference to generators helper**

`src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj` — add:
```xml
<ItemGroup>
  <ProjectReference Include="C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline.Generators\ZeroAlloc.Pipeline.Generators.csproj" />
</ItemGroup>
```

> **Note:** When publishing NuGet, this becomes a `PackageReference` to `ZeroAlloc.Pipeline.Generators` and the DLL must be bundled into the analyzer package using a `build/` MSBuild targets file (see Task 9 note).

**Step 2: Delete `PipelineBehaviorInfo.cs`**

Delete `src/ZeroAlloc.Mediator.Generator/PipelineBehaviorInfo.cs` entirely. The shared `ZeroAlloc.Pipeline.Generators.PipelineBehaviorInfo` replaces it.

**Step 3: Add `using` to `MediatorGenerator.cs`**

At the top of `MediatorGenerator.cs`, add:
```csharp
using ZeroAlloc.Pipeline.Generators;
```

**Step 4: Replace `GetPipelineBehaviorInfo()` with incremental pipeline collection**

In `MediatorGenerator.Initialize()`, replace the `pipelineBehaviors` pipeline:

```csharp
// BEFORE (remove this):
var pipelineBehaviors = context.SyntaxProvider.CreateSyntaxProvider(
    predicate: static (node, _) => node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
    transform: static (ctx, ct) => GetPipelineBehaviorInfo(ctx, ct))
    .Where(static x => x != null)
    .Collect();
```

```csharp
// AFTER (replace with):
var compilationProvider = context.CompilationProvider;

// Pipeline behaviors are collected once per compilation via the shared discoverer.
var pipelineBehaviors = compilationProvider.Select(static (compilation, _) =>
    PipelineBehaviorDiscoverer.Discover(compilation)
        .OrderBy(b => b.Order)
        .ToImmutableArray());
```

Also remove the now-unused `GetPipelineBehaviorInfo()` private static method (the entire method body, lines ~201-263 in the original).

**Step 5: Update `combined` to use new pipeline provider**

The shape of `combined` changes slightly because `pipelineBehaviors` is now a `compilationProvider.Select(...)` instead of a `SyntaxProvider.Collect()`. Update the combine chain accordingly — the `ImmutableArray<PipelineBehaviorInfo?>` type changes to `ImmutableArray<PipelineBehaviorInfo>` (no nulls, the discoverer never returns null entries).

Update `ReportDiagnostics` and `GenerateMediatorClass` call sites to use `ImmutableArray<PipelineBehaviorInfo>` (remove the `?` and the `.Where(x => x != null).Select(x => x!)` filtering).

**Step 6: Build and run tests**

```bash
dotnet build C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx
dotnet test C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -v minimal
```
Expected: All tests pass.

**Step 7: Commit**

```bash
git -C C:\Projects\Prive\ZMediator add .
git -C C:\Projects\Prive\ZMediator commit -m "refactor: replace pipeline discovery with PipelineBehaviorDiscoverer from ZeroAlloc.Pipeline.Generators"
```

---

### Task 9: Migrate the generator — replace emission with shared emitter

Replace `EmitSendMethod()`'s pipeline nesting logic with `PipelineEmitter.EmitChain()`.

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`

**Step 1: Replace the pipeline nesting block inside `EmitSendMethod()`**

Locate `EmitSendMethod()`. The `else` branch that builds the nested pipeline string (the `innermost`/`result` loop) becomes a single `PipelineEmitter.EmitChain()` call.

Replace the `else` branch:
```csharp
// BEFORE (the entire else block with innermost/result loop) — remove it.

// AFTER:
else
{
    var shape = new PipelineShape
    {
        TypeArguments = [handler.RequestTypeName, handler.ResponseTypeName],
        OuterParameterNames = ["request", "ct"],
        LambdaParameterPrefixes = ["r", "c"],
        InnermostBodyTemplate = string.Format(
            "{{ var handler = {0}?.Invoke() ?? new {1}(); return handler.Handle(r{2}, c{2}); }}",
            GetFactoryFieldName(handler.HandlerTypeName),
            handler.HandlerTypeName,
            applicablePipelines.Count),
    };

    var chain = PipelineEmitter.EmitChain(applicablePipelines, shape);
    sb.AppendLine(string.Format("            return {0};", chain));
}
```

**Step 2: Build and run tests**

```bash
dotnet build C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx
dotnet test C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -v minimal
```
Expected: All tests pass. Generated output should be byte-for-byte identical to before for all existing test cases.

**Step 3: Commit**

```bash
git -C C:\Projects\Prive\ZMediator add .
git -C C:\Projects\Prive\ZMediator commit -m "refactor: replace pipeline emission with PipelineEmitter from ZeroAlloc.Pipeline.Generators"
```

---

### Task 10: Migrate diagnostics — use shared rules for ZAM005/ZAM006

ZAM005 (missing Handle method) and ZAM006 (duplicate order) keep their IDs and messages but delegate detection logic to `PipelineDiagnosticRules`.

**Files:**
- Modify: `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs`

**Step 1: Update `ReportDiagnostics()` to use shared helpers**

Locate the ZAM005 and ZAM006 sections in `ReportDiagnostics()`. Replace them:

```csharp
// ZAM005: Missing behavior Handle method (2 type params expected for Send pipeline)
foreach (var behavior in PipelineDiagnosticRules.FindMissingHandleMethod(validBehaviors, expectedTypeParamCount: 2))
{
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.MissingBehaviorHandleMethod,
        Location.None,
        behavior.BehaviorTypeName));
}

// ZAM006: Duplicate behavior order
foreach (var group in PipelineDiagnosticRules.FindDuplicateOrders(validBehaviors))
{
    var behaviorNames = string.Join(", ", group.Select(b => b.BehaviorTypeName));
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.DuplicateBehaviorOrder,
        Location.None,
        behaviorNames,
        group.Key));
}
```

**Step 2: Build and run tests**

```bash
dotnet build C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx
dotnet test C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -v minimal
```
Expected: All tests pass including any existing analyzer diagnostic tests.

**Step 3: Full solution rebuild to confirm no regressions**

```bash
dotnet build C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -c Release
dotnet test C:\Projects\Prive\ZMediator\ZeroAlloc.Mediator.slnx -c Release -v minimal
```
Expected: Build succeeded, all tests pass.

**Step 4: Commit**

```bash
git -C C:\Projects\Prive\ZMediator add .
git -C C:\Projects\Prive\ZMediator commit -m "refactor: use PipelineDiagnosticRules for ZAM005 and ZAM006 detection"
```

---

## Summary of Deliverables

| Package | Location | What it provides |
|---------|----------|-----------------|
| `ZeroAlloc.Pipeline` | `C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline` | `IPipelineBehavior`, `PipelineBehaviorAttribute` |
| `ZeroAlloc.Pipeline.Generators` | `C:\Projects\Prive\ZeroAlloc.Pipeline\src\ZeroAlloc.Pipeline.Generators` | `PipelineBehaviorInfo`, `PipelineBehaviorDiscoverer`, `PipelineShape`, `PipelineEmitter`, `PipelineDiagnosticRules` |
| ZeroAlloc.Mediator (updated) | `C:\Projects\Prive\ZMediator` | Non-breaking — shim types extend pipeline types; generator delegates to shared helpers |

ZeroAlloc.Validation can now reference `ZeroAlloc.Pipeline` for `IValidationBehavior : IPipelineBehavior` and use `PipelineEmitter` in its generator to wrap `Validate(T)` calls — using `PipelineShape` with `TypeArguments = [modelType]`, `OuterParameterNames = ["instance"]`, `LambdaParameterPrefixes = ["r"]`.
