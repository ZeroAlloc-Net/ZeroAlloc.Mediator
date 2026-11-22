---
id: migrating-to-v7
title: Migrating to 7.0
slug: /docs/migrating-to-v7
description: Upgrade from ZeroAlloc.Mediator 6.x to 7.0 — .NET 10 and 11 only, assembly scanners removed, pipeline behaviors take the service provider as state, exception messages out of spans by default, ZAM009 an error.
sidebar_position: 11
---

# Migrating to 7.0

7.0 does not change how requests are dispatched. Five things can break a build or change what your telemetry shows.

## Pipeline behaviors take the pipeline state

`IMediator.Send` used to allocate one `next` delegate per behavior per call, because its lambdas read the provider it was resolved from. 7.0 threads that provider through `next` as the pipeline state, so every lambda of the generated chain is `static` and the compiler creates each once. A `Send` through two awaiting behaviors plus the Cache, Validation and Resilience bridges allocated 392 B per call in 6.x and allocates 0 B in 7.0. The price is the signature of every behavior you wrote: `Handle` gets the state as its last type parameter and its second argument, and passes it on to `next`.

```csharp
// 6.x
public static ValueTask<TResponse> Handle<TRequest, TResponse>(
    TRequest request, CancellationToken ct,
    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    where TRequest : IRequest<TResponse>
    => next(request, ct);

// 7.0
public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
    TRequest request, TState state, CancellationToken ct,
    Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
    where TRequest : IRequest<TResponse>
    => next(request, state, ct);
```

A behavior whose `Handle` has the old two type parameters is reported as the error [ZAM005](diagnostics.md#zam005--pipeline-behavior-missing-handle-method) and left out of the pipeline, including one in a referenced package that was built for 6.x: update that package to its 7.0 release. ZAM005 checks the access and the number of type parameters. A `Handle` with three type parameters but the old parameter list, without the state argument, is not reported as ZAM005: the generated `Send` fails to compile instead, at the call that passes the state.

The state is the service provider of the request being dispatched. `IMediator.Send` passes the provider it was resolved from, and the static `Mediator` class passes `null`. This replaces three 6.x ways of reaching it, which are removed:

- The `Handle(request, IServiceProvider services, ct, next)` overload. Take the state instead: add `where TState : IServiceProvider?` and read `state`. One `Handle` now serves both dispatch paths, so a behavior no longer has a second overload for the static class.
- `PipelineBehaviorServices.Current`. Read `state`. The `AsyncLocal` write behind it, 72 B per `Send`, is gone with it.
- `[PipelineBehavior(UsesPipelineBehaviorServices = false)]`. Delete the argument: nothing is set for a behavior to opt out of.

```csharp
// 6.x
public static ValueTask<TResponse> Handle<TRequest, TResponse>(
    TRequest request, IServiceProvider services, CancellationToken ct,
    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
    where TRequest : IRequest<TResponse>
{
    services.GetService<IAuditLog>()?.Record(request);
    return next(request, ct);
}

// 7.0
public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
    TRequest request, TState state, CancellationToken ct,
    Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
    where TRequest : IRequest<TResponse>
    where TState : IServiceProvider?
{
    state?.GetService<IAuditLog>()?.Record(request);
    return next(request, state, ct);
}
```

A behavior that read `PipelineBehaviorServices.Current` and got `null` for a static dispatch now gets a `null` state.

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
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request, TState state, CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        => next(request, state, ct);
}
```

If the attribute is on a class that was never meant to be a behavior, remove the attribute. See [ZAM009](diagnostics.md#zam009--pipeline-behavior-does-not-implement-ipipelinebehavior).
