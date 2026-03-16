# Changelog

## [1.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/v0.1.5...v1.0.0) (2026-03-16)

First stable release. API is considered production-ready.

### Features

* stable public API: `IRequest<T>`, `INotification`, `IStreamRequest<T>`, `IPipelineBehavior`, `IMediator`, `MediatorService`, `Unit`
* Native AOT compatibility confirmed
* Targets net8.0 (LTS) and net10.0

### Documentation

* extensive consumer documentation with real-world examples, Mermaid diagrams, and cookbook recipes

---

## [0.1.5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/v0.1.4...v0.1.5) (2026-03-16)


### Documentation

* add compiler diagnostics reference ([ecb0215](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/ecb0215c185f9425ff894e31a1d888bc5dad6b81))
* add CQRS web API cookbook ([12be9b6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/12be9b6327e4a26639a6ad95b51e145f4a84f302))
* add dependency injection guide ([a2deb6d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/a2deb6d0418080c245b0a935cc4aec56be1e622a))
* add documentation index ([ead6667](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/ead66670c53c4250a39df2370bc0e3c47a64b2c8))
* add event-driven cookbook ([c7acc88](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/c7acc883f24dfdb2f14567ace5c4242943e7a63e))
* add extensive consumer documentation with real-world examples ([62bc934](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/62bc93486b4e784cbe468b9ca0fc9f9045d782a5))
* add getting started guide ([77903e5](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/77903e5d3d886d539944c34045831b108fe7cb3f))
* add notifications guide ([bf58e66](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/bf58e6631391df8e69e7d8aaba34384df61d0759))
* add performance guide ([a487aa2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/a487aa28011d062eb9e96b9f12b22ba300779ada))
* add pipeline behaviors guide ([9dcf802](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/9dcf802028d09464a776c496cb08ee30596c7e19))
* add pre-push review report ([41a1204](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/41a12044a50ed43aee8238618959fdde4cb0bc26))
* add requests and handlers guide ([52cf18d](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/52cf18d2728fe4a8639a167bf4fe0ca513321d95))
* add streaming guide ([4a554b8](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/4a554b8c4330e0dc82beb46b4514b7717f8c191b))
* add streaming pagination cookbook ([a497f56](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/a497f563903c2a81a835c90a4dc94d43aa2464dc))
* add testing handlers cookbook ([720d4ac](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/720d4ac7a2a15f78e20385eca9d6e702755973bd))
* add transactional pipeline cookbook ([d3f0c18](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/d3f0c184a7e0a3aea25162d002e833f92a3c1076))
* add validation pipeline cookbook ([555c3fc](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/555c3fcc2616a5d7aaf64caf3f60ede206d639c2))

## [0.1.4](https://github.com/MarcelRoozekrans/ZMediator/compare/v0.1.3...v0.1.4) (2026-03-15)


### Features

* rebrand to ZeroAlloc.Mediator ([8dad6e8](https://github.com/MarcelRoozekrans/ZMediator/commit/8dad6e8339fc55880b79799decea987b0de739dc))
* ZeroAlloc.Mediator rebrand, .NET 8 support, and Native AOT ([64b4935](https://github.com/MarcelRoozekrans/ZMediator/commit/64b49353177ef6ea36ca14e936d29e1652e40e0b))


### Bug Fixes

* revert generated namespace to ZeroAlloc, move test namespace to ZeroAlloc.MediatorTests ([059a3c4](https://github.com/MarcelRoozekrans/ZMediator/commit/059a3c4ca0f058018b76618294b4f88590677336))


### Refactoring

* move all types to namespace ZeroAlloc.Mediator ([f8f7f37](https://github.com/MarcelRoozekrans/ZMediator/commit/f8f7f37c39e69e3e0b979d7c3e12f705a3725b29))
* remove old ZMediator paths from git tracking ([f43ca96](https://github.com/MarcelRoozekrans/ZMediator/commit/f43ca96c2efd25352eb4cec9b87fb4d4609a4fa3))
* rename all project folders and files to ZeroAlloc.Mediator ([deffc18](https://github.com/MarcelRoozekrans/ZMediator/commit/deffc18dcdc9f425b21ac6b0a7cc44ad1586ef74))
* rename core library namespace ZMediator -&gt; ZeroAlloc ([610ddc8](https://github.com/MarcelRoozekrans/ZMediator/commit/610ddc8deebd51fade0d84559849a4c000689a60))
* rename diagnostic IDs ZM00x -&gt; ZAM00x and update category to ZeroAlloc.Mediator ([ff6a02c](https://github.com/MarcelRoozekrans/ZMediator/commit/ff6a02c040eb9853609b8a14d23ada8087d913ea))
* rename generator info record namespaces ([a6f8e69](https://github.com/MarcelRoozekrans/ZMediator/commit/a6f8e693d90a94d15d4649de713d0c7e45c4fe94))
* rename MediatorGenerator class and update ZMediator string literals ([476450b](https://github.com/MarcelRoozekrans/ZMediator/commit/476450b0ccf56e2d500aecf5beaf45942b6e24f4))
* update all csproj PackageId, RootNamespace, and ProjectReference paths ([652137e](https://github.com/MarcelRoozekrans/ZMediator/commit/652137e0e379f7ae07ab353c29b5ba69facea413))
* update sample and benchmark namespaces and using directives ([ba893e6](https://github.com/MarcelRoozekrans/ZMediator/commit/ba893e6d535b48e8133ca4847d001ebdba78ad63))
* update solution file for ZeroAlloc.Mediator ([75bd489](https://github.com/MarcelRoozekrans/ZMediator/commit/75bd4894376e6f0db570879c619afc89cfcc03e3))
* update stale ZM00x comment labels to ZAM00x in ReportDiagnostics ([52e7e42](https://github.com/MarcelRoozekrans/ZMediator/commit/52e7e423657e317d83514ab22edf4b226a0fc568))
* update test namespaces, diagnostic IDs, and generated type name assertions ([caf85bc](https://github.com/MarcelRoozekrans/ZMediator/commit/caf85bc4e17a7c145dc346f9bc133ef1b0a57545))


### Documentation

* add rebrand design doc for ZeroAlloc.Mediator ([8de34ae](https://github.com/MarcelRoozekrans/ZMediator/commit/8de34aef45ddaba1ada4945ec77d5bcb8e0df58b))
* add ZeroAlloc.Mediator rebrand implementation plan ([03aa46f](https://github.com/MarcelRoozekrans/ZMediator/commit/03aa46fa1b1b58069a4f89e69e871a9cf51529f3))
* document Native AOT compatibility in README and csproj ([85a850f](https://github.com/MarcelRoozekrans/ZMediator/commit/85a850f4f5a8584ce9bf76f86184574b000d8742))
* update GitHub issue templates and PR template for ZeroAlloc.Mediator rebrand ([2fe9f94](https://github.com/MarcelRoozekrans/ZMediator/commit/2fe9f941d9a989ceac87c4e98cf96a9d80379760))
* update README for ZeroAlloc.Mediator rebrand ([44ffa26](https://github.com/MarcelRoozekrans/ZMediator/commit/44ffa263928225701f1d9954c30a9748787bf905))

## [0.1.3](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/v0.1.2...v0.1.3) (2026-03-10)


### Features

* add .NET 8 LTS support and use PAT for release-please ([afdee05](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/afdee05bc560cfdd3e01023ebf15bd5ca77aa7d9))
* add .NET 8 LTS support via multi-targeting ([9402802](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/940280215bda8668acc00117e4ec4b8ee06c03a2))

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
