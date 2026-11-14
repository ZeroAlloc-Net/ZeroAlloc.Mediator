# AGENTS.md: ZeroAlloc.Mediator

A source-generated MediatR alternative for .NET 8, 10 and 11. Consumers reference the runtime
package `ZeroAlloc.Mediator` plus the source generator `ZeroAlloc.Mediator.Generator`, which emits a
static `Mediator` class with one `Send`/`Publish`/`CreateStream` overload per request type, plus
`AddMediator()` and `IMediator`. Five bridge packages, Validation, Authorization, Cache, Resilience
and Telemetry, add pipeline behaviors. All seven `src/` projects ship to NuGet from one release.

## Layout

- `src/ZeroAlloc.Mediator.Generator/MediatorGenerator.cs` holds the generator and the ZAM
  diagnostics; `DiagnosticDescriptors.cs` defines them. ZAM004 and ZAM007 were removed in 6.0:
  do not reuse those ids.
- The generator packs `ZeroAlloc.Pipeline.Generators.dll` and `ZeroAlloc.Pipeline.dll` next to itself
  under `analyzers/dotnet/cs`, never under `lib/`. `scripts/verify-generator-pack-layout.sh` checks
  that in CI and in the release publish.
- `samples/ZeroAlloc.Mediator.AotSmoke` is the NativeAOT smoke: one folder per bridge, all run from
  `Program.cs`. A new bridge needs a scenario there and a `ProjectReference` with
  `SetTargetFramework="TargetFramework=net11.0"`. Its class-based requests are deliberate; ZAM003 is
  suppressed in `samples/` and `tests/` for that reason.
- `samples/ZeroAlloc.Mediator.AspNetSample` is referenced by `ZeroAlloc.Mediator.Tests`, so it
  multi-targets `net10.0;net11.0` with the tests.
- `tests/ZeroAlloc.Mediator.Benchmarks` compares against MediatR through the generator.
  `tests/ZeroAlloc.Mediator.Benchmarks.Dispatch` has no generator reference on purpose: it compares
  hand-written dispatch shapes, results in its `RESULTS.md`.
- `docs/plans/` holds dated design and plan documents; `docs/backlog.md` the candidate work.

## Build and test

`global.json` pins SDK 11.0.100 with `rollForward: latestMinor`. Libraries target
`net8.0;net10.0;net11.0`, tests `net10.0;net11.0`, benchmarks `net10.0`. From `ci.yml`:

```bash
dotnet tool restore
dotnet restore
dotnet build --no-restore -c Release
dotnet test --no-build -c Release --verbosity normal --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

Pass `--collect` together with `--settings`: with the runsettings file alone a module's coverage can
read 0 at random. The coverage gate fails the job below the line coverage floor.

Inner loop, one project and framework:

```bash
dotnet test tests/ZeroAlloc.Mediator.Tests -c Release -f net11.0 --filter "FullyQualifiedName~HotPathAllocation"
```

AOT smoke, from the `aot-smoke` job in `ci.yml`; it needs clang and `zlib1g-dev`:

```bash
dotnet publish samples/ZeroAlloc.Mediator.AotSmoke/ZeroAlloc.Mediator.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
./aot-out/ZeroAlloc.Mediator.AotSmoke
```

Local pack: `-p:PackageVersion` alone fails with ZAVERSION002, because the assembly is not stamped.
Pass `-p:Version` to the build that the pack uses, then check the layout:

```bash
dotnet pack src/ZeroAlloc.Mediator.Generator/ZeroAlloc.Mediator.Generator.csproj -c Release -p:Version=9.9.9-local.<sha> -o ./artifacts
bash scripts/verify-generator-pack-layout.sh ./artifacts
```

## Conventions and gotchas

- Allocation budgets, all asserted with `AllocationGate`:
  - 0 B: static `Mediator.Send` through a pipeline behavior, static and `IMediator` `Publish` to one
    or two handlers, parallel `Publish` that completes synchronously, a successful `Send` with or
    without exception descriptions, a stateless handler with no factory on the static `Mediator`,
    and a Cache hit on an `ISpanFormattable` request. A Cache hit on a record request allocates
    only its `ToString`.
  - 32 B: `AuthorizationBehavior.Handle`, allow and `IAuthorizedRequest` deny path, for the
    `ResourceSecurityContextAdapter`.
  - 184 B: `IMediator.Send` through the authorization pipeline: 128 B of `next` delegates, 24 B
    transient handler, 32 B adapter. The delegates go away with ZeroAlloc.Pipeline#121; until then
    `IMediator.Send` with a pipeline has no 0 B gate. Do not claim one.
- Test projects with gates set `<Optimize>true</Optimize>`: a Debug async state machine allocates
  248 to 344 B and fails the gates. `Cache.Tests` also sets `TieredCompilation=false`, because tier-0
  code boxes struct requests. Add both when you add a gate to another test project.
- `Mediator.Configure`, `SetFactory`, `PipelineBehaviorServices` and the mediator's `ActivitySource`
  are process-wide, so tests that touch them run in a `DisableParallelization` collection. An
  `ActivityListener` attached by a parallel test makes every dispatch allocate an `Activity` and
  breaks the 0 B gates.
- Every project that references the generator as a `ProjectReference` also needs
  `ZeroAlloc.Pipeline.Generators` as an analyzer and `ZeroAlloc.Pipeline`, both at
  `$(ZeroAllocPipelineVersion)`. That property in `Directory.Build.props` is the single version for
  ZeroAlloc.Pipeline; do not lower it below 1.4.0, which ZeroAlloc.Validation requires.
- Public API is tracked per package in `PublicAPI.Unshipped.txt`; RS0016 and RS0017 are errors. A
  new or changed ZAM rule goes in `AnalyzerReleases.Unshipped.md`. The release PR moves both to
  Shipped; do not edit the Shipped files by hand. New ZAM rules are also documented in
  `docs/diagnostics.md`.
- A new incremental step in the generator gets a name in `TrackingNames.cs` and an entry in
  `GeneratorStepNames` in `IncrementalityTests.cs`, which checks that an unrelated edit stays cached.
- Generator tests assert on generated source strings through `GeneratorTestHelper`; there are no
  snapshot files. Use `RunGeneratorAndCompile` when the emitted code must also compile.
- A `Nullable<T>` cache response can hang under NativeAOT, dotnet/runtime#134799; the smoke runs
  with a 120 s timeout for that reason.
- `apicompat-suppressions.xml` lists only the shims removed in 6.0. The shared api-compat job
  applies it; do not add entries to get past a break.

<!-- org-core:start sha256=c42b83cb821d67a5547aafe02bbf9d3f36a7d19379a5d378bc38d452b7db8451 (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` |
| `Task.WhenAll` over tasks that already completed | check `IsCompletedSuccessfully`, await only what is pending |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)`, `decimal.GetBits(v)` | `Enum.IsDefined<E>`, `v.Scale` |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, exclude only that code with `WarningsNotAsErrors` and name the assembly.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
