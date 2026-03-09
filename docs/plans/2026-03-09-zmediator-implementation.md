# ZMediator Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a zero-allocation mediator library for .NET 10 using a Roslyn incremental source generator.

**Architecture:** Core abstractions package (interfaces/attributes) referenced by consumer projects. A separate source generator project discovers handlers at compile time and emits a static `Mediator` class with strongly-typed dispatch methods. Pipeline behaviors are inlined as nested static calls. No reflection, no DI container.

**Tech Stack:** .NET 10, C# 13, Roslyn Incremental Source Generators, xUnit, BenchmarkDotNet

---

### Task 1: Solution and Project Scaffolding

**Files:**
- Create: `ZMediator.sln`
- Create: `Directory.Build.props`
- Create: `src/ZMediator/ZMediator.csproj`
- Create: `src/ZMediator.Generator/ZMediator.Generator.csproj`
- Create: `tests/ZMediator.Tests/ZMediator.Tests.csproj`
- Create: `tests/ZMediator.Benchmarks/ZMediator.Benchmarks.csproj`
- Create: `samples/ZMediator.Sample/ZMediator.Sample.csproj`

**Step 1: Create solution and projects**

```bash
dotnet new sln -n ZMediator
mkdir -p src/ZMediator src/ZMediator.Generator tests/ZMediator.Tests tests/ZMediator.Benchmarks samples/ZMediator.Sample
dotnet new classlib -n ZMediator -o src/ZMediator --framework net10.0
dotnet new classlib -n ZMediator.Generator -o src/ZMediator.Generator --framework netstandard2.0
dotnet new xunit -n ZMediator.Tests -o tests/ZMediator.Tests --framework net10.0
dotnet new console -n ZMediator.Benchmarks -o tests/ZMediator.Benchmarks --framework net10.0
dotnet new console -n ZMediator.Sample -o samples/ZMediator.Sample --framework net10.0
```

**Step 2: Add projects to solution**

```bash
dotnet sln add src/ZMediator/ZMediator.csproj
dotnet sln add src/ZMediator.Generator/ZMediator.Generator.csproj
dotnet sln add tests/ZMediator.Tests/ZMediator.Tests.csproj
dotnet sln add tests/ZMediator.Benchmarks/ZMediator.Benchmarks.csproj
dotnet sln add samples/ZMediator.Sample/ZMediator.Sample.csproj
```

**Step 3: Create Directory.Build.props (shared analyzers)**

Create `Directory.Build.props` in the repository root to share analyzer packages across all net10.0 projects:

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' != 'netstandard2.0'">
    <PackageReference Include="Meziantou.Analyzer" Version="3.0.19">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Roslynator.Analyzers" Version="4.15.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ErrorProne.NET.CoreAnalyzers" Version="0.1.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ErrorProne.NET.Structs" Version="0.1.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="NetFabric.Hyperlinq.Analyzer" Version="2.3.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="ZeroAlloc.Analyzers" Version="1.0.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

The `Condition="'$(TargetFramework)' != 'netstandard2.0'"` ensures these analyzers are NOT applied to the source generator project (which must target netstandard2.0).

**Step 4: Configure ZMediator.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ZMediator</RootNamespace>
  </PropertyGroup>
</Project>
```

**Step 5: Configure ZMediator.Generator.csproj**

The source generator must target `netstandard2.0` and reference Roslyn analyzers. It must NOT reference the core ZMediator project directly (it reads symbols at compile time). It needs `EnforceExtendedAnalyzerRules`.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>ZMediator.Generator</RootNamespace>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 6: Configure ZMediator.Tests.csproj**

Tests reference both the core lib (for interfaces) and the generator (as analyzer). Also reference a test helper project or inline test types.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ZMediator.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.Testing" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZMediator\ZMediator.csproj" />
    <ProjectReference Include="..\..\src\ZMediator.Generator\ZMediator.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Step 7: Configure ZMediator.Benchmarks.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>ZMediator.Benchmarks</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZMediator\ZMediator.csproj" />
    <ProjectReference Include="..\..\src\ZMediator.Generator\ZMediator.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Step 8: Configure ZMediator.Sample.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <RootNamespace>ZMediator.Sample</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZMediator\ZMediator.csproj" />
    <ProjectReference Include="..\..\src\ZMediator.Generator\ZMediator.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Step 9: Verify solution builds**

Run: `dotnet build ZMediator.sln`
Expected: Build succeeded with 0 errors. Warnings about unused default files are OK.

**Step 10: Delete auto-generated Class1.cs files**

```bash
rm src/ZMediator/Class1.cs
rm src/ZMediator.Generator/Class1.cs
```

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution with core, generator, tests, benchmarks, and sample projects"
```

---

### Task 2: Core Abstractions — Unit Type and Request Interfaces

**Files:**
- Create: `src/ZMediator/Unit.cs`
- Create: `src/ZMediator/IRequest.cs`
- Create: `src/ZMediator/IRequestHandler.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/UnitTests.cs`:

```csharp
namespace ZMediator.Tests;

public class UnitTests
{
    [Fact]
    public void Unit_IsReadonlyRecordStruct()
    {
        var unit = new Unit();
        Assert.Equal(default(Unit), unit);
        Assert.Equal(unit, new Unit());
    }

    [Fact]
    public void Unit_Value_ReturnsSingleton()
    {
        var a = Unit.Value;
        var b = Unit.Value;
        Assert.Equal(a, b);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~UnitTests" --no-restore`
Expected: FAIL — `Unit` type does not exist.

**Step 3: Implement Unit**

Create `src/ZMediator/Unit.cs`:

```csharp
namespace ZMediator;

/// <summary>
/// Represents a void return type for requests that don't return a value.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
```

**Step 4: Implement IRequest**

Create `src/ZMediator/IRequest.cs`:

```csharp
namespace ZMediator;

public interface IRequest<TResponse>;

public interface IRequest : IRequest<Unit>;
```

**Step 5: Implement IRequestHandler**

Create `src/ZMediator/IRequestHandler.cs`:

```csharp
namespace ZMediator;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Handle(TRequest request, CancellationToken ct);
}
```

**Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~UnitTests"`
Expected: 2 passed, 0 failed.

**Step 7: Commit**

```bash
git add src/ZMediator/Unit.cs src/ZMediator/IRequest.cs src/ZMediator/IRequestHandler.cs tests/ZMediator.Tests/UnitTests.cs
git commit -m "feat: add Unit type and request/handler interfaces"
```

---

### Task 3: Core Abstractions — Notifications

**Files:**
- Create: `src/ZMediator/INotification.cs`
- Create: `src/ZMediator/INotificationHandler.cs`
- Create: `src/ZMediator/ParallelNotificationAttribute.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/NotificationInterfaceTests.cs`:

```csharp
namespace ZMediator.Tests;

public class NotificationInterfaceTests
{
    private readonly record struct TestNotification(string Message) : INotification;

    private class TestHandler : INotificationHandler<TestNotification>
    {
        public string? ReceivedMessage { get; private set; }

        public ValueTask Handle(TestNotification notification, CancellationToken ct)
        {
            ReceivedMessage = notification.Message;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task NotificationHandler_CanHandleNotification()
    {
        var handler = new TestHandler();
        await handler.Handle(new TestNotification("hello"), CancellationToken.None);
        Assert.Equal("hello", handler.ReceivedMessage);
    }

    [Fact]
    public void ParallelNotificationAttribute_CanBeApplied()
    {
        var attr = typeof(ParallelTestNotification)
            .GetCustomAttributes(typeof(ParallelNotificationAttribute), false);
        Assert.Single(attr);
    }

    [ParallelNotification]
    private readonly record struct ParallelTestNotification : INotification;
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~NotificationInterfaceTests"`
Expected: FAIL — types do not exist.

**Step 3: Implement INotification and INotificationHandler**

Create `src/ZMediator/INotification.cs`:

```csharp
namespace ZMediator;

public interface INotification;
```

Create `src/ZMediator/INotificationHandler.cs`:

```csharp
namespace ZMediator;

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    ValueTask Handle(TNotification notification, CancellationToken ct);
}
```

Create `src/ZMediator/ParallelNotificationAttribute.cs`:

```csharp
namespace ZMediator;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ParallelNotificationAttribute : Attribute;
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~NotificationInterfaceTests"`
Expected: 2 passed, 0 failed.

**Step 5: Commit**

```bash
git add src/ZMediator/INotification.cs src/ZMediator/INotificationHandler.cs src/ZMediator/ParallelNotificationAttribute.cs tests/ZMediator.Tests/NotificationInterfaceTests.cs
git commit -m "feat: add notification interfaces and ParallelNotification attribute"
```

---

### Task 4: Core Abstractions — Streaming

**Files:**
- Create: `src/ZMediator/IStreamRequest.cs`
- Create: `src/ZMediator/IStreamRequestHandler.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/StreamInterfaceTests.cs`:

```csharp
namespace ZMediator.Tests;

public class StreamInterfaceTests
{
    private readonly record struct CountRequest(int Count) : IStreamRequest<int>;

    private class CountHandler : IStreamRequestHandler<CountRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < request.Count; i++)
            {
                yield return i;
            }
        }
    }

    [Fact]
    public async Task StreamHandler_YieldsExpectedValues()
    {
        var handler = new CountHandler();
        var results = new List<int>();
        await foreach (var item in handler.Handle(new CountRequest(3), CancellationToken.None))
        {
            results.Add(item);
        }
        Assert.Equal([0, 1, 2], results);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~StreamInterfaceTests"`
Expected: FAIL — types do not exist.

**Step 3: Implement streaming interfaces**

Create `src/ZMediator/IStreamRequest.cs`:

```csharp
namespace ZMediator;

public interface IStreamRequest<out TResponse>;
```

Create `src/ZMediator/IStreamRequestHandler.cs`:

```csharp
namespace ZMediator;

public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken ct);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~StreamInterfaceTests"`
Expected: 1 passed, 0 failed.

**Step 5: Commit**

```bash
git add src/ZMediator/IStreamRequest.cs src/ZMediator/IStreamRequestHandler.cs tests/ZMediator.Tests/StreamInterfaceTests.cs
git commit -m "feat: add streaming request interfaces"
```

---

### Task 5: Core Abstractions — Pipeline Behavior

**Files:**
- Create: `src/ZMediator/IPipelineBehavior.cs`
- Create: `src/ZMediator/PipelineBehaviorAttribute.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/PipelineBehaviorAttributeTests.cs`:

```csharp
namespace ZMediator.Tests;

public class PipelineBehaviorAttributeTests
{
    [Fact]
    public void PipelineBehaviorAttribute_DefaultOrder_IsZero()
    {
        var attr = new PipelineBehaviorAttribute();
        Assert.Equal(0, attr.Order);
        Assert.Null(attr.AppliesTo);
    }

    [Fact]
    public void PipelineBehaviorAttribute_WithOrder_SetsOrder()
    {
        var attr = new PipelineBehaviorAttribute(5);
        Assert.Equal(5, attr.Order);
    }

    [Fact]
    public void PipelineBehaviorAttribute_WithAppliesTo_SetsType()
    {
        var attr = new PipelineBehaviorAttribute { AppliesTo = typeof(string) };
        Assert.Equal(typeof(string), attr.AppliesTo);
    }

    [Fact]
    public void IPipelineBehavior_IsMarkerInterface()
    {
        // IPipelineBehavior should have no members
        var members = typeof(IPipelineBehavior).GetMembers(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);
        Assert.Empty(members);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~PipelineBehaviorAttributeTests"`
Expected: FAIL — types do not exist.

**Step 3: Implement pipeline behavior types**

Create `src/ZMediator/IPipelineBehavior.cs`:

```csharp
namespace ZMediator;

public interface IPipelineBehavior;
```

Create `src/ZMediator/PipelineBehaviorAttribute.cs`:

```csharp
namespace ZMediator;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PipelineBehaviorAttribute(int order = 0) : Attribute
{
    public int Order { get; } = order;
    public Type? AppliesTo { get; set; }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~PipelineBehaviorAttributeTests"`
Expected: 4 passed, 0 failed.

**Step 5: Commit**

```bash
git add src/ZMediator/IPipelineBehavior.cs src/ZMediator/PipelineBehaviorAttribute.cs tests/ZMediator.Tests/PipelineBehaviorAttributeTests.cs
git commit -m "feat: add pipeline behavior marker interface and attribute"
```

---

### Task 6: Source Generator — Scaffolding and Request Handler Discovery

**Files:**
- Create: `src/ZMediator.Generator/ZMediatorGenerator.cs`
- Create: `src/ZMediator.Generator/RequestHandlerInfo.cs`

This is the first source generator task. The generator will:
1. Find all types implementing `IRequestHandler<TRequest, TResponse>`
2. Extract request type, response type, and handler type
3. Emit a basic `Mediator.Send()` overload (no pipeline yet)

**Step 1: Write the test**

Source generator tests use `Microsoft.CodeAnalysis.CSharp.Testing`. Create `tests/ZMediator.Tests/GeneratorTests/RequestDispatchGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZMediator.Tests.GeneratorTests;

public class RequestDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsSendMethod_ForRequestHandler()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult($"Pong: {request.Message}");
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static ValueTask<string> Send(global::TestApp.Ping request", output);
    }

    [Fact]
    public void Generator_EmitsSendMethod_ForVoidRequest()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct DoSomething : IRequest;

            public class DoSomethingHandler : IRequestHandler<DoSomething, Unit>
            {
                public ValueTask<Unit> Handle(DoSomething request, CancellationToken ct)
                    => ValueTask.FromResult(Unit.Value);
            }
            """;

        var (output, diagnostics) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static ValueTask<global::ZMediator.Unit> Send(global::TestApp.DoSomething request", output);
    }

    private static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add ZMediator core assembly
        references.Add(MetadataReference.CreateFromFile(typeof(IRequest<>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZMediatorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZMediator"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }
}
```

Add `using System.Collections.Immutable;` at the top of the file.

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~RequestDispatchGeneratorTests"`
Expected: FAIL — `ZMediatorGenerator` does not exist.

**Step 3: Implement RequestHandlerInfo**

Create `src/ZMediator.Generator/RequestHandlerInfo.cs`:

```csharp
namespace ZMediator.Generator;

internal readonly record struct RequestHandlerInfo(
    string RequestFullName,
    string ResponseFullName,
    string HandlerFullName,
    string HandlerClassName,
    bool HasParameterlessConstructor);
```

**Step 4: Implement ZMediatorGenerator — request discovery and Send emission**

Create `src/ZMediator.Generator/ZMediatorGenerator.cs`:

```csharp
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZMediator.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class ZMediatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var requestHandlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetRequestHandlerInfo(ctx, ct))
            .Where(static info => info.HasValue)
            .Select(static (info, _) => info!.Value);

        var collected = requestHandlers.Collect();

        context.RegisterSourceOutput(collected, static (spc, handlers) =>
        {
            if (handlers.IsDefaultOrEmpty) return;
            var source = GenerateMediatorClass(handlers);
            spc.AddSource("Mediator.g.cs", source);
        });
    }

    private static RequestHandlerInfo? GetRequestHandlerInfo(
        GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null) return null;

        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface is not INamedTypeSymbol { IsGenericType: true } namedType)
                continue;

            var originalDef = namedType.OriginalDefinition.ToDisplayString();
            if (originalDef != "ZMediator.IRequestHandler<TRequest, TResponse>")
                continue;

            var requestType = namedType.TypeArguments[0];
            var responseType = namedType.TypeArguments[1];

            var hasParameterlessCtor = symbol.Constructors
                .Any(c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);

            return new RequestHandlerInfo(
                RequestFullName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ResponseFullName: responseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                HandlerFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                HandlerClassName: symbol.Name,
                HasParameterlessConstructor: hasParameterlessCtor);
        }

        return null;
    }

    private static string GenerateMediatorClass(ImmutableArray<RequestHandlerInfo> handlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("public static partial class Mediator");
        sb.AppendLine("{");

        foreach (var handler in handlers)
        {
            var factoryFieldName = $"_{char.ToLowerInvariant(handler.HandlerClassName[0])}{handler.HandlerClassName.Substring(1)}Factory";

            sb.AppendLine($"    private static Func<{handler.HandlerFullName}>? {factoryFieldName};");
            sb.AppendLine();
            sb.AppendLine($"    public static ValueTask<{handler.ResponseFullName}> Send({handler.RequestFullName} request, CancellationToken ct = default)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var handler = {factoryFieldName}?.Invoke() ?? new {handler.HandlerFullName}();");
            sb.AppendLine("        return handler.Handle(request, ct);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~RequestDispatchGeneratorTests"`
Expected: 2 passed, 0 failed.

**Step 6: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add source generator with request handler discovery and Send dispatch"
```

---

### Task 7: Source Generator — Notification Handler Discovery and Publish Emission

**Files:**
- Create: `src/ZMediator.Generator/NotificationHandlerInfo.cs`
- Modify: `src/ZMediator.Generator/ZMediatorGenerator.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/GeneratorTests/NotificationDispatchGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class NotificationDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsSequentialPublish_ForNotification()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int UserId) : INotification;

            public class SendEmailHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class LogHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static async ValueTask Publish(global::TestApp.UserCreated notification", output);
        Assert.Contains("await", output);
    }

    [Fact]
    public void Generator_EmitsParallelPublish_ForParallelNotification()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            [ParallelNotification]
            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class AnalyticsHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class EmailHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Task.WhenAll", output);
    }
}
```

**Step 2: Extract shared test helper**

Create `tests/ZMediator.Tests/GeneratorTests/GeneratorTestHelper.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

internal static class GeneratorTestHelper
{
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(IRequest<>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZMediatorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZMediator"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }
}
```

Also update `RequestDispatchGeneratorTests.cs` to use the shared helper (remove the private `RunGenerator` method and use `GeneratorTestHelper.RunGenerator`).

**Step 3: Implement NotificationHandlerInfo**

Create `src/ZMediator.Generator/NotificationHandlerInfo.cs`:

```csharp
namespace ZMediator.Generator;

internal readonly record struct NotificationHandlerInfo(
    string NotificationFullName,
    string HandlerFullName,
    string HandlerClassName,
    bool HasParameterlessConstructor,
    bool IsParallel);
```

**Step 4: Add notification discovery and Publish emission to ZMediatorGenerator**

Add a second pipeline in `Initialize()` for notification handlers. Combine both pipelines into a single source output. The generator should:
1. Find all `INotificationHandler<T>` implementations
2. Group them by notification type
3. Check if the notification type has `[ParallelNotification]`
4. Emit sequential `await` calls (default) or `Task.WhenAll` (parallel)

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~NotificationDispatchGeneratorTests"`
Expected: 2 passed, 0 failed.

**Step 6: Run all tests**

Run: `dotnet test tests/ZMediator.Tests`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add notification handler discovery with sequential and parallel dispatch"
```

---

### Task 8: Source Generator — Stream Handler Discovery and CreateStream Emission

**Files:**
- Create: `src/ZMediator.Generator/StreamHandlerInfo.cs`
- Modify: `src/ZMediator.Generator/ZMediatorGenerator.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/GeneratorTests/StreamDispatchGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class StreamDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsCreateStream_ForStreamHandler()
    {
        var source = """
            using ZMediator;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;

            namespace TestApp;

            public readonly record struct CountRequest(int Max) : IStreamRequest<int>;

            public class CountHandler : IStreamRequestHandler<CountRequest, int>
            {
                public async IAsyncEnumerable<int> Handle(
                    CountRequest request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    for (var i = 0; i < request.Max; i++)
                        yield return i;
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static System.Collections.Generic.IAsyncEnumerable<int> CreateStream(global::TestApp.CountRequest request", output);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~StreamDispatchGeneratorTests"`
Expected: FAIL — CreateStream not emitted.

**Step 3: Implement StreamHandlerInfo**

Create `src/ZMediator.Generator/StreamHandlerInfo.cs`:

```csharp
namespace ZMediator.Generator;

internal readonly record struct StreamHandlerInfo(
    string RequestFullName,
    string ResponseFullName,
    string HandlerFullName,
    string HandlerClassName,
    bool HasParameterlessConstructor);
```

**Step 4: Add stream discovery and CreateStream emission to ZMediatorGenerator**

Add a third pipeline in `Initialize()` for `IStreamRequestHandler<TRequest, TResponse>`. The generator should:
1. Find all `IStreamRequestHandler<T, R>` implementations
2. Emit a `CreateStream()` overload returning `IAsyncEnumerable<R>`

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~StreamDispatchGeneratorTests"`
Expected: 1 passed, 0 failed.

**Step 6: Run all tests**

Run: `dotnet test tests/ZMediator.Tests`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add stream handler discovery and CreateStream dispatch"
```

---

### Task 9: Source Generator — Pipeline Behavior Discovery and Inlining

**Files:**
- Create: `src/ZMediator.Generator/PipelineBehaviorInfo.cs`
- Modify: `src/ZMediator.Generator/ZMediatorGenerator.cs`

This is the most complex generator task. The generator must:
1. Find all classes with `[PipelineBehavior]` that implement `IPipelineBehavior`
2. Sort by `Order`
3. For each `Send()` method, inline behaviors as nested static lambda calls
4. Handle scoped behaviors (only apply to specific request types)

**Step 1: Write the test**

Create `tests/ZMediator.Tests/GeneratorTests/PipelineBehaviorGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class PipelineBehaviorGeneratorTests
{
    [Fact]
    public void Generator_InlinesPipelineBehaviors_InOrder()
    {
        var source = """
            using ZMediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }

            [PipelineBehavior(Order = 1)]
            public class ValidationBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // LoggingBehavior (order=0) should wrap ValidationBehavior (order=1) which wraps handler
        Assert.Contains("LoggingBehavior", output);
        Assert.Contains("ValidationBehavior", output);
        // Logging should appear before Validation in the nested chain
        var loggingIdx = output.IndexOf("LoggingBehavior.Handle", StringComparison.Ordinal);
        var validationIdx = output.IndexOf("ValidationBehavior.Handle", StringComparison.Ordinal);
        Assert.True(loggingIdx < validationIdx, "LoggingBehavior should wrap ValidationBehavior");
    }

    [Fact]
    public void Generator_ScopedBehavior_OnlyAppliedToTargetRequest()
    {
        var source = """
            using ZMediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;
            public readonly record struct Pong(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            public class PongHandler : IRequestHandler<Pong, string>
            {
                public ValueTask<string> Handle(Pong request, CancellationToken ct)
                    => ValueTask.FromResult("Ping");
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Ping))]
            public class PingOnlyBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Split output to find Send methods for Ping vs Pong
        var pingSendIdx = output.IndexOf("Send(global::TestApp.Ping", StringComparison.Ordinal);
        var pongSendIdx = output.IndexOf("Send(global::TestApp.Pong", StringComparison.Ordinal);
        var pingSection = output.Substring(pingSendIdx, pongSendIdx - pingSendIdx);
        var pongSection = output.Substring(pongSendIdx);

        Assert.Contains("PingOnlyBehavior", pingSection);
        Assert.DoesNotContain("PingOnlyBehavior", pongSection);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~PipelineBehaviorGeneratorTests"`
Expected: FAIL — behaviors not inlined.

**Step 3: Implement PipelineBehaviorInfo**

Create `src/ZMediator.Generator/PipelineBehaviorInfo.cs`:

```csharp
namespace ZMediator.Generator;

internal readonly record struct PipelineBehaviorInfo(
    string BehaviorFullName,
    string BehaviorClassName,
    int Order,
    string? AppliesToFullName);
```

**Step 4: Update ZMediatorGenerator**

Add pipeline behavior discovery:
1. Find classes with `[PipelineBehavior]` that implement `IPipelineBehavior`
2. Extract `Order` and `AppliesTo` from the attribute
3. Pass behaviors to the `Send` method generator
4. For each request, filter applicable behaviors (global + matching scoped), sort by order (global before scoped at same order), and emit nested `static` lambda calls

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~PipelineBehaviorGeneratorTests"`
Expected: 2 passed, 0 failed.

**Step 6: Run all tests**

Run: `dotnet test tests/ZMediator.Tests`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add pipeline behavior discovery and compile-time inlining"
```

---

### Task 10: Source Generator — Configure and Factory Registration

**Files:**
- Modify: `src/ZMediator.Generator/ZMediatorGenerator.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/GeneratorTests/ConfigurationGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class ConfigurationGeneratorTests
{
    [Fact]
    public void Generator_EmitsConfigureMethod()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static void Configure(", output);
        Assert.Contains("MediatorConfig", output);
        Assert.Contains("SetFactory", output);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~ConfigurationGeneratorTests"`
Expected: FAIL — Configure not emitted.

**Step 3: Add Configure and MediatorConfig generation**

Update the generator to emit:
1. A `Configure(Action<MediatorConfig> configure)` static method
2. A `MediatorConfig` class with a `SetFactory<THandler>(Func<THandler> factory)` method
3. Inside `SetFactory`, emit a type check for each known handler to assign the correct static factory field

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~ConfigurationGeneratorTests"`
Expected: 1 passed, 0 failed.

**Step 5: Run all tests**

Run: `dotnet test tests/ZMediator.Tests`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add Configure and factory registration to generated mediator"
```

---

### Task 11: Analyzer Diagnostics

**Files:**
- Modify: `src/ZMediator.Generator/ZMediatorGenerator.cs`
- Create: `src/ZMediator.Generator/DiagnosticDescriptors.cs`

**Step 1: Write the test**

Create `tests/ZMediator.Tests/GeneratorTests/DiagnosticTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void ZM002_DuplicateHandler_EmitsError()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler1 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong1");
            }

            public class PingHandler2 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong2");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm002 = diagnostics.FirstOrDefault(d => d.Id == "ZM002");
        Assert.NotNull(zm002);
        Assert.Equal(DiagnosticSeverity.Error, zm002.Severity);
    }

    [Fact]
    public void ZM003_ClassRequest_EmitsWarning()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public class Ping : IRequest<string> { }

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm003 = diagnostics.FirstOrDefault(d => d.Id == "ZM003");
        Assert.NotNull(zm003);
        Assert.Equal(DiagnosticSeverity.Warning, zm003.Severity);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~DiagnosticTests"`
Expected: FAIL — diagnostics not emitted.

**Step 3: Create DiagnosticDescriptors**

Create `src/ZMediator.Generator/DiagnosticDescriptors.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace ZMediator.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor DuplicateHandler = new(
        "ZM002",
        "Duplicate request handler",
        "Request type '{0}' has multiple handlers: {1}",
        "ZMediator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ClassRequest = new(
        "ZM003",
        "Request type is a class",
        "Request type '{0}' is a class. Use 'readonly record struct' for zero-allocation dispatch",
        "ZMediator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidHandlerSignature = new(
        "ZM004",
        "Invalid handler signature",
        "Handler '{0}' does not match the expected method signature",
        "ZMediator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingBehaviorHandleMethod = new(
        "ZM005",
        "Missing Handle method on pipeline behavior",
        "Pipeline behavior '{0}' is missing a static Handle<TRequest, TResponse> method",
        "ZMediator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateBehaviorOrder = new(
        "ZM006",
        "Duplicate pipeline behavior order",
        "Multiple pipeline behaviors have Order={0}: {1}. Execution order is ambiguous",
        "ZMediator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

**Step 4: Wire diagnostics into the generator**

Update `ZMediatorGenerator` to:
1. After collecting request handlers, group by request type — if any group has >1 handler, report ZM002
2. For each request type that is a class (not struct/record struct), report ZM003
3. For each `[PipelineBehavior]` class, verify it has a static `Handle` method — if not, report ZM005

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~DiagnosticTests"`
Expected: 2 passed, 0 failed.

**Step 6: Run all tests**

Run: `dotnet test tests/ZMediator.Tests`
Expected: All tests pass.

**Step 7: Commit**

```bash
git add src/ZMediator.Generator/ tests/ZMediator.Tests/GeneratorTests/
git commit -m "feat: add analyzer diagnostics ZM002 and ZM003"
```

---

### Task 12: Integration Tests — End-to-End Dispatch

**Files:**
- Create: `tests/ZMediator.Tests/IntegrationTests/RequestIntegrationTests.cs`
- Create: `tests/ZMediator.Tests/IntegrationTests/NotificationIntegrationTests.cs`
- Create: `tests/ZMediator.Tests/IntegrationTests/StreamIntegrationTests.cs`

These tests define real handlers in the test project and verify the source generator wires them correctly (the generated `Mediator` class should exist and dispatch correctly at compile time).

**Step 1: Write request integration test**

Create `tests/ZMediator.Tests/IntegrationTests/RequestIntegrationTests.cs`:

```csharp
namespace ZMediator.Tests.IntegrationTests;

public readonly record struct Ping(string Message) : IRequest<string>;

public class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken ct)
        => ValueTask.FromResult($"Pong: {request.Message}");
}

public class RequestIntegrationTests
{
    [Fact]
    public async Task Send_DispatchesToHandler()
    {
        var result = await Mediator.Send(new Ping("Hello"), CancellationToken.None);
        Assert.Equal("Pong: Hello", result);
    }
}
```

**Step 2: Write notification integration test**

Create `tests/ZMediator.Tests/IntegrationTests/NotificationIntegrationTests.cs`:

```csharp
namespace ZMediator.Tests.IntegrationTests;

public readonly record struct TestEvent(string Data) : INotification;

public class TestEventHandlerA : INotificationHandler<TestEvent>
{
    public static string? LastData;
    public ValueTask Handle(TestEvent notification, CancellationToken ct)
    {
        LastData = notification.Data + "_A";
        return ValueTask.CompletedTask;
    }
}

public class TestEventHandlerB : INotificationHandler<TestEvent>
{
    public static string? LastData;
    public ValueTask Handle(TestEvent notification, CancellationToken ct)
    {
        LastData = notification.Data + "_B";
        return ValueTask.CompletedTask;
    }
}

public class NotificationIntegrationTests
{
    [Fact]
    public async Task Publish_DispatchesToAllHandlers()
    {
        TestEventHandlerA.LastData = null;
        TestEventHandlerB.LastData = null;

        await Mediator.Publish(new TestEvent("test"), CancellationToken.None);

        Assert.Equal("test_A", TestEventHandlerA.LastData);
        Assert.Equal("test_B", TestEventHandlerB.LastData);
    }
}
```

**Step 3: Write stream integration test**

Create `tests/ZMediator.Tests/IntegrationTests/StreamIntegrationTests.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace ZMediator.Tests.IntegrationTests;

public readonly record struct Numbers(int Count) : IStreamRequest<int>;

public class NumbersHandler : IStreamRequestHandler<Numbers, int>
{
    public async IAsyncEnumerable<int> Handle(
        Numbers request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Count; i++)
            yield return i;
    }
}

public class StreamIntegrationTests
{
    [Fact]
    public async Task CreateStream_YieldsExpectedValues()
    {
        var results = new List<int>();
        await foreach (var n in Mediator.CreateStream(new Numbers(3), CancellationToken.None))
            results.Add(n);

        Assert.Equal([1, 2, 3], results);
    }
}
```

**Step 4: Run all integration tests**

Run: `dotnet test tests/ZMediator.Tests --filter "FullyQualifiedName~IntegrationTests"`
Expected: 3 passed, 0 failed.

**Step 5: Commit**

```bash
git add tests/ZMediator.Tests/IntegrationTests/
git commit -m "test: add end-to-end integration tests for request, notification, and stream dispatch"
```

---

### Task 13: Sample Application

**Files:**
- Create: `samples/ZMediator.Sample/Program.cs`

**Step 1: Write sample application**

Create `samples/ZMediator.Sample/Program.cs`:

```csharp
using ZMediator;
using System.Runtime.CompilerServices;

// === Request/Response ===
var pong = await Mediator.Send(new Ping("Hello ZMediator!"), CancellationToken.None);
Console.WriteLine(pong);

// === Notification ===
await Mediator.Publish(new UserCreated(42, "Alice"), CancellationToken.None);

// === Streaming ===
Console.Write("Counting: ");
await foreach (var n in Mediator.CreateStream(new CountTo(5), CancellationToken.None))
{
    Console.Write($"{n} ");
}
Console.WriteLine();

// === Request types ===
public readonly record struct Ping(string Message) : IRequest<string>;
public readonly record struct UserCreated(int Id, string Name) : INotification;
public readonly record struct CountTo(int Max) : IStreamRequest<int>;

// === Handlers ===
public class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken ct)
        => ValueTask.FromResult($"Pong: {request.Message}");
}

public class UserCreatedLogger : INotificationHandler<UserCreated>
{
    public ValueTask Handle(UserCreated notification, CancellationToken ct)
    {
        Console.WriteLine($"User created: {notification.Name} (ID: {notification.Id})");
        return ValueTask.CompletedTask;
    }
}

public class CountToHandler : IStreamRequestHandler<CountTo, int>
{
    public async IAsyncEnumerable<int> Handle(
        CountTo request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Max; i++)
            yield return i;
    }
}
```

**Step 2: Run sample**

Run: `dotnet run --project samples/ZMediator.Sample`
Expected output:
```
Pong: Hello ZMediator!
User created: Alice (ID: 42)
Counting: 1 2 3 4 5
```

**Step 3: Commit**

```bash
git add samples/ZMediator.Sample/
git commit -m "feat: add sample application demonstrating request, notification, and stream dispatch"
```

---

### Task 14: Benchmarks

**Files:**
- Modify: `tests/ZMediator.Benchmarks/Program.cs`

**Step 1: Write benchmarks**

Create `tests/ZMediator.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ZMediator;

BenchmarkRunner.Run<MediatorBenchmarks>();

[MemoryDiagnoser]
public class MediatorBenchmarks
{
    private readonly CancellationToken _ct = CancellationToken.None;

    [Benchmark]
    public ValueTask<string> Send_SimpleRequest()
    {
        return Mediator.Send(new BenchPing("test"), _ct);
    }

    [Benchmark]
    public ValueTask Publish_SingleHandler()
    {
        return Mediator.Publish(new BenchEvent("test"), _ct);
    }
}

public readonly record struct BenchPing(string Message) : IRequest<string>;
public readonly record struct BenchEvent(string Data) : INotification;

public class BenchPingHandler : IRequestHandler<BenchPing, string>
{
    public ValueTask<string> Handle(BenchPing request, CancellationToken ct)
        => ValueTask.FromResult(request.Message);
}

public class BenchEventHandler : INotificationHandler<BenchEvent>
{
    public ValueTask Handle(BenchEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}
```

**Step 2: Verify benchmarks build**

Run: `dotnet build tests/ZMediator.Benchmarks -c Release`
Expected: Build succeeded.

**Step 3: Run benchmarks (short)**

Run: `dotnet run --project tests/ZMediator.Benchmarks -c Release -- --job short`
Expected: Output showing 0 bytes allocated per operation for both benchmarks.

**Step 4: Commit**

```bash
git add tests/ZMediator.Benchmarks/
git commit -m "feat: add BenchmarkDotNet benchmarks verifying zero-allocation dispatch"
```

---

### Task 15: Final Verification

**Step 1: Run all tests**

Run: `dotnet test ZMediator.sln`
Expected: All tests pass.

**Step 2: Run sample**

Run: `dotnet run --project samples/ZMediator.Sample`
Expected: Correct output.

**Step 3: Verify benchmarks build**

Run: `dotnet build tests/ZMediator.Benchmarks -c Release`
Expected: Build succeeded.

**Step 4: Final commit (if any cleanup needed)**

```bash
git status
```
