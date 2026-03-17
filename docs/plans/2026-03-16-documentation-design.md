---
date: 2026-03-16
topic: Consumer Documentation
status: approved
---

# ZeroAlloc.Mediator — Documentation Design

## Goal

Write extensive consumer-facing documentation for `ZeroAlloc.Mediator` with real-world examples and Mermaid diagrams, split into logical files under `docs/`.

## Audience

Library consumers: .NET developers who want to use `ZeroAlloc.Mediator` in their projects.

## Structure

### Reference Docs (`docs/`)

| File | Contents |
|------|----------|
| `01-getting-started.md` | Installation, first request in 5 minutes |
| `02-requests.md` | `IRequest<TResponse>`, `IRequest` (Unit), handlers, real-world examples |
| `03-notifications.md` | Sequential, parallel (`[ParallelNotification]`), polymorphic handlers |
| `04-streaming.md` | `IStreamRequest<T>`, `IAsyncEnumerable` patterns |
| `05-pipeline-behaviors.md` | `IPipelineBehavior`, ordering, scoping with `AppliesTo` |
| `06-dependency-injection.md` | `IMediator`, `MediatorService`, `Mediator.Configure()`, DI containers |
| `07-diagnostics.md` | ZAM001–ZAM007 error reference with fix guidance |
| `08-performance.md` | Zero-alloc rationale, benchmark results, struct request guidance |

### Cookbook (`docs/cookbook/`)

| File | Scenario |
|------|----------|
| `01-cqrs-web-api.md` | ASP.NET Core Minimal API with commands & queries |
| `02-event-driven.md` | Domain events, fan-out, audit trails |
| `03-validation-pipeline.md` | FluentValidation as a pipeline behavior |
| `04-transactional-pipeline.md` | EF Core transactions wrapping handlers |
| `05-streaming-pagination.md` | Large dataset streaming via `IAsyncEnumerable` |
| `06-testing-handlers.md` | Unit testing handlers and behaviors in isolation |

## Per-Document Standards

- Each file has at least one Mermaid diagram (flow or sequence)
- Code examples use realistic domain scenarios (orders, users, products — not Ping/Pong)
- "Common pitfalls" callouts where applicable
- Cross-links between related docs
