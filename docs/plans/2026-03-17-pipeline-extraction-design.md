---
date: 2026-03-17
topic: ZeroAlloc.Pipeline extraction
status: approved
---

# ZeroAlloc.Pipeline — Extraction Design

## Goal

Extract the pipeline behavior concept from `ZeroAlloc.Mediator` into a standalone `ZeroAlloc.Pipeline` solution so it can be reused by other ZeroAlloc libraries, starting with `ZeroAlloc.Validation`. All changes to ZeroAlloc.Mediator must be non-breaking.

## Output Location

`C:\Projects\Prive\ZeroAlloc.Pipeline`

---

## Package Design

### `ZeroAlloc.Pipeline` (runtime)

A tiny runtime package — no generator, no reflection, just the shared contract types:

| Type | Namespace | Purpose |
|------|-----------|---------|
| `IPipelineBehavior` | `ZeroAlloc.Pipeline` | Marker interface; all behavior classes implement this |
| `PipelineBehaviorAttribute` | `ZeroAlloc.Pipeline` | `Order` (int), `AppliesTo` (Type?) — controls ordering and optional scoping |

### `ZeroAlloc.Pipeline.Generators` (dev-time helper)

A plain `netstandard2.0` DLL — **not** an analyzer itself. Generator projects reference it as a normal DLL dependency, bundled into their analyzer NuGet package via a `build/` MSBuild target.

| Type | Purpose |
|------|---------|
| `PipelineBehaviorInfo` | Data class: `BehaviorTypeName`, `Order`, `AppliesTo`, `HasValidHandleMethod` |
| `PipelineBehaviorDiscoverer` | Roslyn discovery: finds classes decorated with `[PipelineBehavior]` that implement `IPipelineBehavior` |
| `PipelineEmitter` | Emits nested static call chains; parameterized by delegate shape (return type + parameter types) |
| `PipelineDiagnosticRules` | Reusable detection logic for missing `Handle` method and duplicate `Order` values |

The `PipelineEmitter` is parameterized so each consuming generator passes its own delegate shape:
- ZMediator: `ValueTask<TResponse> Handle<TRequest, TResponse>(TRequest, CancellationToken, Func<...>)`
- ZValidation: `ValidationResult Handle<T>(T, Func<T, ValidationResult>)`

---

## Repo Structure

```
ZeroAlloc.Pipeline/                            # C:\Projects\Prive\ZeroAlloc.Pipeline
├── src/
│   ├── ZeroAlloc.Pipeline/
│   │   ├── IPipelineBehavior.cs
│   │   ├── PipelineBehaviorAttribute.cs
│   │   └── ZeroAlloc.Pipeline.csproj
│   └── ZeroAlloc.Pipeline.Generators/
│       ├── PipelineBehaviorInfo.cs
│       ├── PipelineBehaviorDiscoverer.cs
│       ├── PipelineEmitter.cs
│       ├── PipelineDiagnosticRules.cs
│       └── ZeroAlloc.Pipeline.Generators.csproj
└── tests/
    └── ZeroAlloc.Pipeline.Generators.Tests/
        ├── DiscovererTests.cs
        ├── EmitterTests.cs
        └── ZeroAlloc.Pipeline.Generators.Tests.csproj
```

---

## ZeroAlloc.Mediator Migration (non-breaking)

### Runtime (`ZeroAlloc.Mediator`)

`IPipelineBehavior` and `PipelineBehaviorAttribute` stay in the `ZeroAlloc.Mediator` namespace as thin subclasses — existing consumer code compiles unchanged:

```csharp
// ZeroAlloc.Mediator — no change for consumers
namespace ZeroAlloc.Mediator;
public interface IPipelineBehavior : ZeroAlloc.Pipeline.IPipelineBehavior { }
```

```csharp
namespace ZeroAlloc.Mediator;
public sealed class PipelineBehaviorAttribute(int order = 0)
    : ZeroAlloc.Pipeline.PipelineBehaviorAttribute(order) { }
```

Adds: `<PackageReference Include="ZeroAlloc.Pipeline" />`

### Generator (`ZeroAlloc.Mediator.Generator`)

- Adds: `<PackageReference Include="ZeroAlloc.Pipeline.Generators" />`
- Removes: own `PipelineBehaviorInfo`, `GetPipelineBehaviorInfo()`, `EmitSendMethod()` pipeline sections
- Delegates to: `PipelineBehaviorDiscoverer` and `PipelineEmitter` from shared package
- **ZAM005 and ZAM006 diagnostic descriptors remain in ZMediator** — only detection logic is shared

**Versioning:** non-breaking minor release (e.g. `1.2.0`)

---

## ZeroAlloc.Validation Integration

### Runtime (`ZeroAlloc.Validation`)

Adds a thin behavior interface in its own namespace:

```csharp
namespace ZeroAlloc.Validation;
public interface IValidationBehavior : ZeroAlloc.Pipeline.IPipelineBehavior { }
```

Consumer example:

```csharp
[PipelineBehavior(Order = 0)]
public class CachingBehavior : IValidationBehavior
{
    public static ValidationResult Handle<T>(T instance, Func<T, ValidationResult> next)
    {
        // cache lookup / population
        return next(instance);
    }
}

// AppliesTo scopes to a single model type:
[PipelineBehavior(Order = 1, AppliesTo = typeof(PaymentDetails))]
public class PaymentAuditBehavior : IValidationBehavior { ... }
```

### Generator (`ZeroAlloc.Validation.Generator`)

- Adds: `<PackageReference Include="ZeroAlloc.Pipeline.Generators" />`
- Wraps emitted `Validate(T)` calls with detected behaviors using `PipelineEmitter` (delegate shape: `ValidationResult Handle<T>(T, Func<T, ValidationResult>)`)
- Adds ZV-series diagnostics for missing Handle method and duplicate Order

---

## MSBuild Bundling Pattern

To bundle `ZeroAlloc.Pipeline.Generators.dll` inside the analyzer NuGet packages, each consuming generator project includes a `build/` target:

```xml
<!-- ZeroAlloc.Mediator.Generator.targets (in build/ folder of NuGet) -->
<ItemGroup>
  <Analyzer Include="$(MSBuildThisFileDirectory)../analyzers/ZeroAlloc.Pipeline.Generators.dll" />
</ItemGroup>
```

This is the standard pattern for analyzer dependencies (used by e.g. `Microsoft.CodeAnalysis.Common`).

---

## Testing Strategy

- `ZeroAlloc.Pipeline.Generators.Tests` — unit tests for discoverer and emitter using in-process `CSharpCompilation` (Roslyn compilation API)
- ZMediator and ZValidation existing test suites act as integration tests after migration
- No new test infrastructure needed in either consuming repo
