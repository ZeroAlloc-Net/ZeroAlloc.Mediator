# Design: generated handler registration (#275)

**Date:** 2026-10-09
**Issue:** #275

## Context

The DI quickstart registers handlers with `services.AddMediator().RegisterHandlersFromAssembly(asm)`. That scanner enumerates `assembly.GetTypes()` and is marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the documented DI path is not trim- or AOT-safe. Today the generated `AddMediator()` registers only `IMediator`, and `MediatorService` resolves each handler with `GetRequiredService<THandler>`. So without the scanner, every handler has to be registered by hand.

The generator already discovers every request, notification and stream handler, because it emits the dispatch code. It also emits `AddMediator()` into the handler assembly as an `internal` method, one copy per assembly. That method can register the handlers itself.

ZeroAlloc.Analyzers ZA1710 (1.9.0) tells MediatR users that ZeroAlloc.Mediator dispatches without reflection. This change makes the DI path match that claim.

## Decisions

1. **`AddMediator()` registers the handlers.** The decision was to put the registrations inside the existing method rather than add a separate opt-in call, so the AOT-safe path needs no configuration.
2. **A lifetime overload.** The generator emits `AddMediator(ServiceLifetime defaultHandlerLifetime)`, and `AddMediator()` forwards with `ServiceLifetime.Transient`.
3. **`RegisterHandlersFromAssembly` becomes `[Obsolete]`** as a warning in a minor release. A `next-major` issue tracks removing it.

## Generator changes

- **Handler info.** `RequestHandlerInfo`, `NotificationHandlerInfo` and `StreamHandlerInfo` each gain a lifetime. It is read from `[HandlerLifetime(ServiceLifetime.X)]` on the handler class, and is null when the attribute is absent.
  - Store the enum's integer value, or null; do not store the symbol.
  - The infos must keep their value equality, so incremental caching keeps working.
- **Registration emission.** Inside the generated `AddMediator(ServiceLifetime defaultHandlerLifetime)`, emit one registration per distinct handler type, across all three handler kinds:
  - The call is `services.TryAdd(new ServiceDescriptor(typeof(global::Ns.Handler), typeof(global::Ns.Handler), <lifetime>))`.
  - `<lifetime>` is the handler's `[HandlerLifetime]` value when present, otherwise the `defaultHandlerLifetime` parameter.
  - The service type is the concrete handler type, the same thing the scanner registers and `MediatorService` resolves.
  - A handler class that implements several handler interfaces is registered once. The emitted order is deterministic, sorted by fully qualified type name, so snapshots are stable.
- **The parameterless overload.** `AddMediator()` becomes `=> AddMediator(services, ServiceLifetime.Transient)`. Both overloads are `internal` and live in the existing `MediatorServiceCollectionExtensions` partial class, so neither collides across assemblies.
- **Scope.** Only handlers the generator already accepts are registered: non-generic, accessible, and declared in this compilation. Internal handlers are now registered too; the scanner skipped them, because it only took public types.
- **Text that pointed users at the scanner.** Two runtime messages in the generated code ("No handler registered for…" and "No factory registered for…") and diagnostic `ZAM` text at `DiagnosticDescriptors.cs:52` change to point at `services.AddMediator()`.

## Public API

- `MediatorBuilderExtensions.RegisterHandlersFromAssembly` gets `[Obsolete("AddMediator() now registers every handler in this assembly at compile time; use AddMediator(ServiceLifetime) to change the default lifetime. This reflection-based scanner is not trim- or AOT-safe and will be removed in the next major version.")]`. It is a warning, not an error.
- No other public signature changes. The generated methods are internal, so api-compat is unaffected.

## Behaviour change (release notes)

Consider code that calls `AddMediator().RegisterHandlersFromAssembly(asm, ServiceLifetime.Scoped)`. `AddMediator()` now registers the handlers first, as Transient, and the scanner's `TryAdd` then does nothing. Handlers that were Scoped become Transient, unless they carry `[HandlerLifetime]`.

The fix is to call `AddMediator(ServiceLifetime.Scoped)` and drop the scanner call. The PR body carries this text in a "Behaviour change" section, which the org's release-notes tooling collects.

## Docs and samples

- `docs/dependency-injection.md`:
  - The quickstart becomes `builder.Services.AddMediator();`, and the worker example changes the same way.
  - Add a lifetime subsection covering `AddMediator(ServiceLifetime)` and `[HandlerLifetime]`.
  - Add a short "migrating from RegisterHandlersFromAssembly" note.
  - State that internal handlers are now registered.
- `docs/diagnostics.md` lines 171 and 186: point at `AddMediator()`.
- `README.md` and `docs/getting-started.md`: update any handler-registration snippet.
- `samples/ZeroAlloc.Mediator.AspNetSample/Program.cs`, `tests/ZeroAlloc.Mediator.Benchmarks/Program.cs` and `tests/ZeroAlloc.Mediator.Benchmarks.Dispatch/Program.cs`: use `AddMediator()`. `RESULTS.md` is historical output and is left as is.

## Testing

- **Generator snapshot tests**, through the existing GeneratorSnapshot setup:
  - one handler of each kind, default lifetime;
  - a `[HandlerLifetime(Scoped)]` override;
  - an internal handler;
  - one class implementing two handler interfaces, registered once;
  - no handlers at all, where `AddMediator` still compiles and registers only `IMediator`.
- **Integration tests:**
  - after only `services.AddMediator()`, `IMediator.Send`, `Publish` and `CreateStream` reach their handlers;
  - `AddMediator(ServiceLifetime.Scoped)` gives scoped handlers, the same instance within a scope and different instances across scopes;
  - `[HandlerLifetime(Singleton)]` beats the default;
  - a handler the caller already registered keeps the caller's registration, because of `TryAdd`.
- **Existing tests.** Integration tests move from the scanner to `AddMediator()`. `RegisterHandlersFromAssemblyTests` stays and suppresses CS0618 locally, because it tests the obsolete API itself.
- **AOT.** The existing AOT smoke check, if one exists under `samples/` or `tests/`, must publish and resolve handlers with no scanning.

## Out of scope

- Removing `RegisterHandlersFromAssembly`, which is the next major and gets its own `next-major` issue.
- Keyed registration, and registering handlers under their handler interfaces.
