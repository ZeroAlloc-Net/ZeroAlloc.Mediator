## Summary

<!-- Brief description of the changes and why they are needed -->

## Type of Change

- [ ] `feat` — New feature
- [ ] `fix` — Bug fix
- [ ] `perf` — Performance improvement
- [ ] `refactor` — Code refactoring (no behavior change)
- [ ] `docs` — Documentation only
- [ ] `test` — Adding or updating tests
- [ ] `build` / `ci` — Build system or CI changes
- [ ] `chore` — Maintenance

## Changes

-

## Breaking Changes

<!-- If this is a breaking change, describe what breaks and the migration path -->

None

## Test Plan

- [ ] All existing tests pass (`dotnet test`)
- [ ] New tests added for new functionality
- [ ] Benchmarks verified (no allocation regression)
- [ ] Sample app runs correctly (`dotnet run --project samples/ZeroAlloc.Mediator.Sample`)

## Checklist

- [ ] Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/)
- [ ] Code builds without warnings
- [ ] Generator code targets `netstandard2.0` (no C# 10+ features)
- [ ] Zero-allocation constraint maintained (no heap allocations in generated dispatch)
- [ ] No secrets or credentials in committed files
