---
id: migrating-to-v7
title: Migrating to 7.0
slug: /docs/migrating-to-v7
description: Upgrade from ZeroAlloc.Mediator 6.x to 7.0 — .NET 10 and 11 only, assembly scanners removed, exception messages out of spans by default, ZAM009 an error.
sidebar_position: 11
---

# Migrating to 7.0

7.0 does not change how requests are dispatched. Four things can break a build or change what your telemetry shows.

## .NET 10 and .NET 11 only

Every runtime package now targets `net10.0` and `net11.0`. The `net8.0` assets are gone, because .NET 8 went out of support in November 2026. A project that still targets `net8.0` or `net9.0` stays on 6.x until it moves to .NET 10. The source generator package is unchanged: it still targets `netstandard2.0`, as Roslyn requires.

## RegisterHandlersFromAssembly and RegisterHandlersFromAssemblies are removed

Both scanners have been `[Obsolete]` since 6.2, and `MediatorBuilderExtensions`, the class that held them, is gone too. They scanned assemblies with reflection, so they were not trim- or AOT-safe. The generated `AddMediator()` already registers every handler at compile time, and after it a scanner call did nothing.

Delete the call. If you passed a lifetime, pass it to `AddMediator` instead:

```csharp
// 6.x
services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly, ServiceLifetime.Scoped);

// 7.0
services.AddMediator(ServiceLifetime.Scoped);
```

`[HandlerLifetime]` and ZeroAlloc.Inject's lifetime attributes still override that default per handler. See [Dependency Injection](dependency-injection.md#handler-lifetimes).

## Failed spans no longer carry the exception message

`MediatorTracing.ExceptionDescription` now defaults to `false`. When a generated `Send`, `Publish` or `CreateStream` throws, its span gets `ActivityStatusCode.Error` with no status description; the `error.type` tag still names the exception's type. An exception message can carry request or response content, and every listener on the `ZeroAlloc.Mediator` source receives it, so leaving it out is the safe default. ZeroAlloc.Telemetry's `[Trace]` makes the same change in its 2.0.

If a dashboard or alert reads the status description, opt back in for the assembly whose handlers the generator dispatches:

```csharp
[assembly: ZeroAlloc.Mediator.MediatorTracing(ExceptionDescription = true)]
```

An `[assembly: MediatorTracing(ExceptionDescription = false)]` from 6.x still works and can now be deleted.

## ZAM009 is an error

A `[PipelineBehavior]` class that is `static`, or that does not implement `IPipelineBehavior`, never runs: the generator leaves it out of the pipeline. 6.x reported that as the warning ZAM009; 7.0 reports it as an error, so the build fails instead of shipping a pipeline without the behavior.

Fix the class: make it non-static and implement `IPipelineBehavior`. Only `Handle` stays static.

```csharp
[PipelineBehavior(Order = 0)]
public sealed class LoggingBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        => next(request, ct);
}
```

If the attribute is on a class that was never meant to be a behavior, remove the attribute. See [ZAM009](diagnostics.md#zam009--pipeline-behavior-does-not-implement-ipipelinebehavior).
