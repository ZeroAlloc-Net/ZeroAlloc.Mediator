# Changelog

## [2.0.0](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/compare/authorization-v2.0.0...authorization-v2.0.0) (2026-05-20)


### ⚠ BREAKING CHANGES

* **authorization:** AuthorizationBehavior no longer reads policies via the static MediatorAuthorizationGeneratedHooks delegate plumbing. It now resolves AuthorizerFor<TRequest> from the IServiceProvider — the source generator in ZeroAlloc.Authorization v2.0.0 emits one per [RequirePolicy]-decorated request type.
* IMediator registration is Transient instead of Singleton; the static Mediator dispatch path no longer emits '?? new T()' for handlers without an accessible parameterless constructor (throws InvalidOperationException with remediation guidance instead); IMediatorBuilder.Create static interface method removed (use the now-public MediatorBuilder class directly). See

### Features

* **authorization:** v2.0.0 - split versioning + consume AuthorizerFor&lt;T&gt; via DI ([#87](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/87)) ([a5a50f6](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/a5a50f6fc4fe85111a2bfa3a9c0b6de6051f284e))
* bump to 4.0.0 for [#63](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/63) breaking changes ([#70](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/70)) ([d27cec2](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/d27cec25226defab4021c55eff82e45c45744cd2))
* **mediator.authorization:** new sub-package — request-handler authorization ([#74](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/issues/74)) ([c06a653](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/c06a653ddf43c18397b7635b0f44dfe988e56d44))


### Bug Fixes

* update NuGet package metadata for all packable projects ([da33e91](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator/commit/da33e91b1955eb1b841679eb89f17e7aa1ee8a29))
