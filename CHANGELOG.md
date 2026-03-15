# Changelog

## [0.1.2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/v0.1.1...v0.1.2) (2026-03-10)


### Bug Fixes

* add explicit PackageId to packable projects ([71c40df](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/71c40dff6741d07214abd52d032ea0a2de3b9c70))
* add explicit PackageId to packable projects ([94a587d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/94a587df56ba3b3cb863500837cf38a23d080186))
* add package icon for NuGet listings ([361ccdd](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/361ccdd1bb255e539e0314e63537f5594c3b5b29))
* update NuGet package metadata for all packable projects ([da33e91](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/da33e91b1955eb1b841679eb89f17e7aa1ee8a29))

## [0.1.1](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/v0.1.0...v0.1.1) (2026-03-09)


### Features

* add DI support, polymorphic notifications, MediatR benchmarks, and project scaffolding ([e30a48b](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/e30a48b199a73d32ebd391696f9f5cb53b312d62))
* add notification interfaces and ParallelNotification attribute ([1cbca4c](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/1cbca4c9734f751953a5ff544937239a5889582a))
* add pipeline behavior marker interface and attribute ([490f408](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/490f408856eb0fc3d603d524afdf3b7360ff54ba))
* add streaming request interfaces ([6bd480f](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/6bd480ff1bb63550952ffb6ccf92ca301d05ddf6))
* add Unit type and request/handler interfaces ([f0abc16](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/f0abc1693c59670503a6803f3aacbcb5a4d128ea))
* DI support, polymorphic notifications, and MediatR benchmarks ([e02858d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/e02858d2cf826caebfd796f3acf6ff5e4db65371))
* scaffold solution with core, generator, tests, benchmarks, and sample projects ([26b2456](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/26b245629b91d530f78b336333a522e6334d363b))


### Documentation

* add analyzer packages to implementation plan ([a689865](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/a689865d2fcd6ac0a3df2303a0704aa19e86eafd))
* add ZeroAlloc.Mediator implementation plan ([5ac0eca](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/5ac0ecae5672435d924a8b4e40dd26fe5364f037))
* fix step numbering and remove duplicate properties in plan ([c8f5461](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/c8f5461661979d5694995cb4ac3088f48f56fff5))


### Tests

* add combined generator test verifying all dispatch methods ([40c507d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/40c507d875a673a072bd129328a0e6db32b4e817))

## 0.1.0 (Unreleased)

### Features

- Request/response dispatch with compile-time generated `Send` overloads
- Notification dispatch (sequential and parallel via `[ParallelNotification]`)
- Polymorphic notification dispatch for base type handlers
- Streaming via `IAsyncEnumerable<T>` with `CreateStream`
- Pipeline behaviors inlined at compile time
- Factory delegate configuration for handler dependencies
- Analyzer diagnostics: ZM001-ZM007
