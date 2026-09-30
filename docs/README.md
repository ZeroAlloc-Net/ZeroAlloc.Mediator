---
id: docs-index
title: Documentation
slug: /docs
description: ZeroAlloc.Mediator documentation index — navigate to all available pages.
sidebar_position: 0
---

# ZeroAlloc.Mediator Documentation

Zero-allocation, compile-time-dispatched mediator for .NET 8 and .NET 10.

## Reference

| # | Guide | Description |
|---|-------|-------------|
| 1 | [Getting Started](getting-started.md) | Install and send your first request in 5 minutes |
| 2 | [Requests & Handlers](requests.md) | Commands, queries, `Unit` responses, dispatch |
| 3 | [Notifications](notifications.md) | Events: sequential, parallel, polymorphic handlers |
| 4 | [Streaming](streaming.md) | `IAsyncEnumerable<T>` for large result sets |
| 5 | [Pipeline Behaviors](pipeline-behaviors.md) | Middleware: logging, validation, caching, transactions |
| 6 | [Dependency Injection](dependency-injection.md) | DI containers, `IMediator`, factory delegates |
| 7 | [Diagnostics](diagnostics.md) | ZAM001–ZAM008 compiler error reference with fixes |
| 8 | [Performance](performance.md) | Zero-alloc internals, benchmark results, Native AOT |
| 9 | [Advanced Patterns](advanced.md) | Error handling, cancellation, scoped behaviors |
| 10 | [Testing](testing.md) | Unit-test handlers, behaviors, and notifications |
| 11 | [Migrating to 6.0](migrating-to-v6.md) | Removed v1 registration shims, Validation 2, retired diagnostics |

## Cookbook

Real-world recipes for common scenarios.

| # | Recipe | Scenario |
|---|--------|----------|
| 1 | [CQRS Web API](cookbook/01-cqrs-web-api.md) | ASP.NET Core Minimal API with commands & queries |
| 2 | [Event-Driven Architecture](cookbook/02-event-driven.md) | Domain events, fan-out, polymorphic audit trails |
| 3 | [Validation Pipeline](cookbook/03-validation-pipeline.md) | Hand-rolled or FluentValidation in a behavior |
| 4 | [Transactional Pipeline](cookbook/04-transactional-pipeline.md) | EF Core transactions wrapping command handlers |
| 5 | [Streaming Large Datasets](cookbook/05-streaming-pagination.md) | CSV export, cursor-based pagination |
| 6 | [Testing Handlers](cookbook/06-testing-handlers.md) | Unit and integration tests without a test framework |

## Quick Reference

```csharp
// Request / Response
public readonly record struct PlaceOrderCommand(...) : IRequest<OrderId>;
var id = await Mediator.Send(new PlaceOrderCommand(...));

// Fire-and-forget
public readonly record struct ArchiveOrderCommand(...) : IRequest;
await Mediator.Send(new ArchiveOrderCommand(orderId));

// Notification (event)
public readonly record struct OrderShippedEvent(...) : INotification;
await Mediator.Publish(new OrderShippedEvent(...));

// Parallel notification
[ParallelNotification]
public readonly record struct PaymentReceivedEvent(...) : INotification;

// Streaming
public readonly record struct ExportOrdersQuery(...) : IStreamRequest<OrderCsvRow>;
await foreach (var row in Mediator.CreateStream(new ExportOrdersQuery(...))) { ... }

// Pipeline behavior
[PipelineBehavior(Order = 0)]
public sealed class LoggingBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        => await next(request, ct);
}
```
