# Rebrand: ZMediator → ZeroAlloc.Mediator

**Date:** 2026-03-15
**Status:** Approved

## Summary

Rebrand the project from `ZMediator` to `ZeroAlloc.Mediator` as part of a broader `ZeroAlloc` package ecosystem vision. All packages will share the `ZeroAlloc` root namespace so users need only a single `using ZeroAlloc;` import. The repo will move to the `ZeroAlloc-NET` GitHub org.

## Section 1 — Files & Folders

| Before | After |
|--------|-------|
| `ZMediator.slnx` | `ZeroAlloc.Mediator.slnx` |
| `src/ZMediator/` | `src/ZeroAlloc.Mediator/` |
| `src/ZMediator/ZMediator.csproj` | `src/ZeroAlloc.Mediator/ZeroAlloc.Mediator.csproj` |
| `src/ZMediator.Generator/` | `src/ZeroAlloc.Mediator.Generator/` |
| `src/ZMediator.Generator/ZMediator.Generator.csproj` | `src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj` |
| `tests/ZMediator.Tests/` | `tests/ZeroAlloc.Mediator.Tests/` |
| `tests/ZMediator.Tests/ZMediator.Tests.csproj` | `tests/ZeroAlloc.Mediator.Tests/ZeroAlloc.Mediator.Tests.csproj` |
| `tests/ZMediator.Benchmarks/` | `tests/ZeroAlloc.Mediator.Benchmarks/` |
| `tests/ZMediator.Benchmarks/ZMediator.Benchmarks.csproj` | `tests/ZeroAlloc.Mediator.Benchmarks/ZeroAlloc.Mediator.Benchmarks.csproj` |
| `samples/ZMediator.Sample/` | `samples/ZeroAlloc.Mediator.Sample/` |
| `samples/ZMediator.Sample/ZMediator.Sample.csproj` | `samples/ZeroAlloc.Mediator.Sample/ZeroAlloc.Mediator.Sample.csproj` |

NuGet PackageIds:
- `ZMediator` → `ZeroAlloc.Mediator`
- `ZMediator.Generator` → `ZeroAlloc.Mediator.Generator`

## Section 2 — Namespaces & Type Renames

| Before | After |
|--------|-------|
| `namespace ZMediator` (core library) | `namespace ZeroAlloc` |
| `namespace ZMediator.Generator` (generator internals) | `namespace ZeroAlloc.Mediator.Generator` |
| `RootNamespace` in core csproj | `ZeroAlloc` |
| `RootNamespace` in generator csproj | `ZeroAlloc.Mediator.Generator` |
| `IZMediator` | `IMediator` |
| `ZMediatorService` | `MediatorService` |
| `ZMediatorGenerator` | `MediatorGenerator` |
| Generated file `ZMediator.Mediator.g.cs` | `ZeroAlloc.Mediator.g.cs` |
| Emitted `namespace ZMediator` in generated code | `namespace ZeroAlloc` |
| Generator string literals `"ZMediator.IRequest<TResponse>"` etc. | `"ZeroAlloc.IRequest<TResponse>"` etc. |

### Diagnostic IDs (breaking change)

`ZM001`–`ZM007` → `ZAM001`–`ZAM007`. Document in CHANGELOG as a breaking change.

## Section 3 — CI/CD & GitHub

### Workflow files

- `ci.yml`: update all references to `ZMediator.slnx` → `ZeroAlloc.Mediator.slnx`, benchmark project path updated
- `release.yml`: update solution reference, pack step project paths for both packages

### README

- Update NuGet package references (`ZeroAlloc.Mediator`, `ZeroAlloc.Mediator.Generator`)
- Update DI interface/class names (`IMediator`, `MediatorService`)
- Update project structure tree
- Update diagnostic ID table (`ZAM001`–`ZAM007`)

### GitHub (manual steps after PR merges)

1. Create org `ZeroAlloc-NET` on GitHub
2. Transfer repo `MarcelRoozekrans/ZMediator` → `ZeroAlloc-NET/ZeroAlloc.Mediator`
3. Update local remote: `git remote set-url origin https://github.com/ZeroAlloc-NET/ZeroAlloc.Mediator`

## Non-Goals

- No functional changes to library behaviour
- No API surface changes beyond renames listed above
