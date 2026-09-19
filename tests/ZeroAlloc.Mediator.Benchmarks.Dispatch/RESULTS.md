# Notification dispatch strategy — measured cost

Baseline run for [ZeroAlloc.Saga#127](https://github.com/ZeroAlloc-Net/ZeroAlloc.Saga/issues/127).
`BenchmarkDotNet.Artifacts/` is gitignored, so the numbers are recorded here.

Question being settled: generated `Publish` currently resolves handlers by concrete type only,
which never reaches handlers registered as `INotificationHandler<T>` (what `ZeroAlloc.Saga`
emits). Should the fix enumerate DI **always**, or only when no compile-time handler exists?

## Environment

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2)
12th Gen Intel Core i9-12900HK, 1 CPU, 20 logical / 14 physical cores
.NET SDK 10.0.401, .NET 10.0.12, X64 RyuJIT x86-64-v3
Workstation GC, concurrent
```

Handlers registered `Transient` — the default used by `RegisterHandlersFromAssembly` and by
Saga's `With{Saga}Saga()`.

## Results

| Method | Categories | Mean | Ratio | Gen0 | Allocated | Alloc Ratio |
|--- |--- |---: |---: |---: |---: |---: |
| Concrete_1 | N=1 | 19.54 ns | 1.02 | 0.0019 | 24 B | 1.00 |
| Enumerable_1 | N=1 | 51.88 ns | 2.70 | 0.0069 | 88 B | 3.67 |
| ConcreteAndEnumerable_1 | N=1 | 52.34 ns | 2.72 | 0.0088 | 112 B | 4.67 |
| ConcreteAndEnumerableDeduped_1 | N=1 | 55.63 ns | 2.89 | 0.0088 | 112 B | 4.67 |
| | | | | | | |
| Concrete_3 | N=3 | 41.50 ns | 1.00 | 0.0057 | 72 B | 1.00 |
| Enumerable_3 | N=3 | 96.58 ns | 2.33 | 0.0119 | 152 B | 2.11 |
| ConcreteAndEnumerable_3 | N=3 | 130.85 ns | 3.15 | 0.0178 | 224 B | 3.11 |
| ConcreteAndEnumerableDeduped_3 | N=3 | 150.24 ns | 3.62 | 0.0176 | 224 B | 3.11 |
| | | | | | | |
| Concrete_NoInterfaceRegs | NoInterfaceRegs | 36.24 ns | 1.00 | 0.0019 | 24 B | 1.00 |
| ConcreteAndEmptyEnumerable_NoInterfaceRegs | NoInterfaceRegs | 66.47 ns | 1.83 | 0.0019 | 24 B | 1.00 |

## Conclusion: enumerate unconditionally

The `NoInterfaceRegs` row is the one that decides it. It models every existing Mediator user —
handlers registered by concrete type, nothing registered by interface, so the added enumeration
finds nothing and its entire cost is waste:

- **+30 ns** per publish (36.24 → 66.47)
- **+0 bytes.** Allocation is unchanged at 24 B.

`Microsoft.Extensions.DependencyInjection` returns `Array.Empty<T>()` for an empty service
enumerable, so the tax on users who never touch Saga is pure CPU and leaves the library's
headline zero-allocation promise intact. 30 ns is not a meaningful cost next to any real handler
body, let alone the database write a saga step performs.

Gating the enumeration on "no compile-time handler exists" would save that 30 ns and in exchange
reintroduce the silent no-op documented in #127: a host that declares its own
`INotificationHandler<T>` gets a compile-time handler, so the gate closes, and the saga is
skipped without error. Trading a correctness hole for 30 ns is the wrong side of that trade.

### Secondary observations

- When handlers **are** registered by interface, enumeration costs roughly +30 ns and
  +64…80 B — the `GetServices` array plus transient handler instantiation. Unavoidable with
  MS DI; registering saga handlers as singleton or scoped would reduce it.
- Inline dedup (`if (h is TConcrete) continue;`) allocates nothing. The generator knows every
  concrete handler type at compile time, so it can emit exactly this and avoid the `HashSet`
  a hand-written fix would reach for. Dedup is required: without it, a handler registered under
  both contracts runs twice.

### Caveat on precision

`Concrete_1` (19.54 ns) and `Concrete_NoInterfaceRegs` (36.24 ns) are the *same dispatch shape*
and should agree; they differ by 16 ns. BenchmarkDotNet flagged `Concrete_1`,
`ConcreteAndEnumerable_1` and `ConcreteAndEnumerableDeduped_3` as multimodal. Treat the absolute
deltas within a category as sound and the cross-category ratios as indicative only. The
order-of-magnitude finding — enumeration costs tens of nanoseconds and zero allocation when it
finds nothing — is consistent across every category and is what the decision rests on.

## Reproducing

```
dotnet run -c Release --project tests/ZeroAlloc.Mediator.Benchmarks.Dispatch
```

Runtime is about 10 minutes for the 10 benchmarks.
