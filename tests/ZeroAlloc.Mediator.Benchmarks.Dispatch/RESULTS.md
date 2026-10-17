# Notification dispatch strategy — measured cost

Baseline run for [ZeroAlloc.Saga#127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Saga/issues/127).
`BenchmarkDotNet.Artifacts/` is gitignored, so the numbers are recorded here.

Question being settled: generated `Publish` currently resolves handlers by concrete type only,
which never reaches handlers registered as `INotificationHandler<T>` (what `ZeroAlloc.Saga`
emits). Should the fix enumerate DI **always**, or only when no compile-time handler exists?

## Environment

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
12th Gen Intel Core i9-12900HK 2.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3
```

Re-measured 2026-10-17 out of process in three launches, pinned to two performance cores; the
first run, which settled the question, used one unpinned launch.

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Handlers registered `Transient` — the default used by `RegisterHandlersFromAssembly` and by
Saga's `With{Saga}Saga()`.

## Results

`Ratio` is each row's mean time relative to the concrete-only row of its category in the same
run; `Launch range` is the lowest and highest ratio of the three launches.

| Method | Categories | Ratio | Launch range | Allocated |
|--- |--- |---: |---: |---: |
| Concrete_1 | N=1 | 1.00 | | 24 B |
| Enumerable_1 | N=1 | 2.08 | 2.06–2.09 | 88 B |
| ConcreteAndEnumerable_1 | N=1 | 2.77 | 2.72–2.82 | 112 B |
| ConcreteAndEnumerableDeduped_1 | N=1 | 2.90 | 2.72–3.16 | 112 B |
| | | | | |
| Concrete_3 | N=3 | 1.00 | | 72 B |
| Enumerable_3 | N=3 | 1.29 | 1.03–1.49 | 152 B |
| ConcreteAndEnumerable_3 | N=3 | 2.23 | 1.68–2.92 | 224 B |
| ConcreteAndEnumerableDeduped_3 | N=3 | 2.44 | 1.64–3.07 | 224 B |
| | | | | |
| Concrete_NoInterfaceRegs | NoInterfaceRegs | 1.00 | | 24 B |
| ConcreteAndEmptyEnumerable_NoInterfaceRegs | NoInterfaceRegs | 2.16 | 1.91–2.58 | 24 B |

## Conclusion: enumerate unconditionally

The `NoInterfaceRegs` row is the one that decides it. It models every existing Mediator user —
handlers registered by concrete type, nothing registered by interface, so the added enumeration
finds nothing and its entire cost is waste:

- **About twice the time** of the concrete-only publish (2.16×), a few tens of nanoseconds
- **+0 bytes.** Allocation is unchanged at 24 B.

`Microsoft.Extensions.DependencyInjection` returns `Array.Empty<T>()` for an empty service
enumerable, so the tax on users who never touch Saga is pure CPU and leaves the library's
headline zero-allocation promise intact. Tens of nanoseconds is not a meaningful cost next to any
real handler body, let alone the database write a saga step performs.

Gating the enumeration on "no compile-time handler exists" would save that time and in exchange
reintroduce the silent no-op documented in #127: a host that declares its own
`INotificationHandler<T>` gets a compile-time handler, so the gate closes, and the saga is
skipped without error. Trading a correctness hole for tens of nanoseconds is the wrong side of that trade.

### Secondary observations

- When handlers **are** registered by interface, enumeration costs about 1.3–2.9× the
  concrete-only time and +64…80 B — the `GetServices` array plus transient handler instantiation. Unavoidable with
  MS DI; registering saga handlers as singleton or scoped would reduce it.
- Inline dedup (`if (h is TConcrete) continue;`) allocates nothing. The generator knows every
  concrete handler type at compile time, so it can emit exactly this and avoid the `HashSet`
  a hand-written fix would reach for. Dedup is required: without it, a handler registered under
  both contracts runs twice.

### Caveat on precision

The first run's `Concrete_1` and `Concrete_NoInterfaceRegs` disagreed by 16 ns although they are
the same dispatch shape, and BenchmarkDotNet flagged three rows as multimodal. The re-run's launch
ranges show the same: the N=3 rows vary by up to 1.8× between launches. Treat the ratios within a
category as indicative. The finding the decision rests on — enumeration costs tens of
nanoseconds and zero allocation when it finds nothing — holds in every launch of both runs.

## Reproducing

```
dotnet run -c Release --project tests/ZeroAlloc.Mediator.Benchmarks.Dispatch -- --filter "*" --launchCount 3
```

Runtime is about 11 minutes for the 10 benchmarks with three launches.
