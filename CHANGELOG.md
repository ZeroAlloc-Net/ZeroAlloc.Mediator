# Changelog

## 0.1.0 (Unreleased)

### Features

- Request/response dispatch with compile-time generated `Send` overloads
- Notification dispatch (sequential and parallel via `[ParallelNotification]`)
- Polymorphic notification dispatch for base type handlers
- Streaming via `IAsyncEnumerable<T>` with `CreateStream`
- Pipeline behaviors inlined at compile time
- Factory delegate configuration for handler dependencies
- Analyzer diagnostics: ZM001-ZM007
