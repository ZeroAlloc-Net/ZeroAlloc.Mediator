# Rebrand: ZeroAlloc.Mediator → ZeroAlloc.Mediator

**Date:** 2026-03-15
**Status:** Approved

## Summary

Rebrand the project from `ZeroAlloc.Mediator` to `ZeroAlloc.Mediator` as part of a broader `ZeroAlloc` package ecosystem vision. All packages will share the `ZeroAlloc` root namespace so users need only a single `using ZeroAlloc;` import. The repo will move to the `ZeroAlloc-NET` GitHub org.

## Section 1 — Files & Folders

| Before | After |
|--------|-------|
| `ZeroAlloc.Mediator.slnx` | `ZeroAlloc.Mediator.slnx` |
| `src/ZeroAlloc.Mediator/` | `src/ZeroAlloc.Mediator/` |
| `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj` | `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj` |
| `src/ZeroAlloc.Mediator.Generator/` | `src/ZeroAlloc.Mediator.Generator/` |
| `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj` | `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj` |
| `tests/ZeroAlloc.Mediator.Tests/` | `tests/ZeroAlloc.Mediator.Tests/` |
| `tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj` | `tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj` |
| `tests/ZeroAlloc.Mediator.Benchmarks/` | `tests/ZeroAlloc.Mediator.Benchmarks/` |
| `tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj` | `tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj` |
| `samples/ZeroAlloc.Mediator.Sample/` | `samples/ZeroAlloc.Mediator.Sample/` |
| `samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj` | `samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj` |

NuGet PackageIds:
- `ZeroAlloc.Mediator` → `ZeroAlloc.Mediator`
- `ZeroAlloc.Mediator.Generator` → `ZeroAlloc.Mediator.Generator`

## Section 2 — Namespaces & Type Renames

| Before | After |
|--------|-------|
| `namespace ZeroAlloc.Mediator` (core library) | `namespace ZeroAlloc` |
| `namespace ZeroAlloc.Mediator.Generator` (generator internals) | `namespace ZeroAlloc.Mediator.Generator` |
| `RootNamespace` in core csproj | `ZeroAlloc` |
| `RootNamespace` in generator csproj | `ZeroAlloc.Mediator.Generator` |
| `IZeroAlloc.Mediator` | `IMediator` |
| `ZeroAlloc.MediatorService` | `MediatorService` |
| `ZeroAlloc.MediatorGenerator` | `MediatorGenerator` |
| Generated file `ZeroAlloc.Mediator.Mediator.g.cs` | `ZeroAlloc.Mediator.g.cs` |
| Emitted `namespace ZeroAlloc.Mediator` in generated code | `namespace ZeroAlloc` |
| Generator string literals `"ZeroAlloc.Mediator.IRequest<TResponse>"` etc. | `"ZeroAlloc.IRequest<TResponse>"` etc. |

### Diagnostic IDs (breaking change)

`ZM001`–`ZM007` → `ZAM001`–`ZAM007`. Document in CHANGELOG as a breaking change.

## Section 3 — CI/CD & GitHub

### Workflow files

- `ci.yml`: update all references to `ZeroAlloc.Mediator.slnx` → `ZeroAlloc.Mediator.slnx`, benchmark project path updated
- `release.yml`: update solution reference, pack step project paths for both packages

### README

- Update NuGet package references (`ZeroAlloc.Mediator`, `ZeroAlloc.Mediator.Generator`)
- Update DI interface/class names (`IMediator`, `MediatorService`)
- Update project structure tree
- Update diagnostic ID table (`ZAM001`–`ZAM007`)

### GitHub (manual steps after PR merges)

1. Create org `ZeroAlloc-NET` on GitHub
2. Transfer repo `ZeroAlloc-Net/ZeroAlloc.Mediator` → `ZeroAlloc-NET/ZeroAlloc.Mediator`
3. Update local remote: `git remote set-url origin https://github.com/ZeroAlloc-NET/ZeroAlloc.Mediator`

## Non-Goals

- No functional changes to library behaviour
- No API surface changes beyond renames listed above
