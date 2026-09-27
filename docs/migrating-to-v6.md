---
id: migrating-to-v6
title: Migrating to 6.0
slug: /docs/migrating-to-v6
description: Upgrade from ZeroAlloc.Mediator 5.x to 6.0 — Validation 2, removed v1 registration shims, retired diagnostics.
sidebar_position: 11
---

# Migrating to 6.0

6.0 is a clean-up major. Nothing in the dispatch path changes. Three things can break a build.

## ZeroAlloc.Mediator.Validation requires ZeroAlloc.Validation 2

`ZeroAlloc.Mediator.Validation` 6.0 depends on `ZeroAlloc.Validation` 2.x, up from 1.x. If your project references `ZeroAlloc.Validation` directly, upgrade that reference to 2.x together with this package. Validation 2 has breaking changes of its own; see its [migration notes](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/blob/main/docs/migrating-to-v2.md). In particular, drop any direct `ZeroAlloc.Validation.Generator` reference: `ZeroAlloc.Validation` now carries the generator, and keeping both fails the build with ZV9001.

## The v1 registration shims are removed

The `IServiceCollection` extensions from 1.x have been `[Obsolete]` since 2.0 and are now gone. Each one has a direct replacement on the `IMediatorBuilder` that `AddMediator()` returns:

| Removed | Obsolete ID | Replacement |
|---------|-------------|-------------|
| `services.AddMediatorCache()` | ZAMED001 | `services.AddMediator().WithCache()` |
| `services.AddMediatorValidation()` | ZAMED002 | `services.AddMediator().WithValidation()` |
| `services.AddMediatorResilience()` | ZAMED003 | `services.AddMediator().WithResilience()` |

```csharp
// 5.x, with ZAMED001–ZAMED003 warnings
services.AddMediator();
services.AddMediatorCache();
services.AddMediatorValidation();
services.AddMediatorResilience();

// 6.0
services.AddMediator()
        .WithCache()
        .WithValidation()
        .WithResilience();
```

The shims registered only the bridge's own services and never `IMediator`, so a 5.x setup already had to call `AddMediator()` as well. The builder methods register exactly what the shims did, and are idempotent.

Remove any `ZAMED001`, `ZAMED002` or `ZAMED003` entries from `NoWarn` or `#pragma warning disable`; the IDs no longer exist.

## ZAM004 and ZAM007 are retired

The generator declared ZAM004 (invalid handler signature) and ZAM007 (stream handler wrong return type) but never reported them: a handler whose `Handle` method does not match its interface fails to compile with `CS0535` or `CS0738` first. 6.0 removes both descriptors. Delete any suppression that names them; it never had an effect. See [Removed diagnostics](diagnostics.md#removed-diagnostics).
