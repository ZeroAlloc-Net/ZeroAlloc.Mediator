# Mediator.Authorization — resource-based context adoption

**Date:** 2026-05-23
**Scope:** Light up `IResourceSecurityContext<TResource>` (the dormant contract shipped today in `ZeroAlloc.Authorization` v2.1) in `Mediator.Authorization`. The behaviour now exposes the dispatched request as the typed resource so resource-based authorization policies fire correctly through Mediator.

## Background

- `ZeroAlloc.Authorization` v2.1 (PR #28, merged today) shipped `IResourceSecurityContext<TResource>` as a public interface plus the `OwnerOnlyPolicy`-style consumer pattern: policies type-check `ctx is IResourceSecurityContext<Post> rc` to access `rc.Resource`.
- The contract was deliberately documented as *dormant* in v2.1: the interface exists, but no host populates it. Consumer policies that type-check for it fall through to the `false` branch until a host opts in.
- `Mediator.Authorization`'s `AuthorizationBehavior.Handle<TRequest, TResponse>` is the natural first host: it already resolves an `ISecurityContext` from DI per dispatch, then calls `AuthorizerFor<TRequest>.EvaluateAsync(ctx, ct)`. Replacing the plain `ctx` with an adapter that ALSO carries the dispatched `TRequest` as the resource is a small, fully-additive change.

## Goal

Make `IResourceSecurityContext<TRequest>` light up automatically for every Mediator-dispatched request, with no consumer code changes required beyond writing the resource-based policy.

## Decisions

### 1. The resource IS the dispatched request

`TResource = TRequest` always. In CQRS-style Mediator usage the request IS the operation
plus its parameters — that's the natural "thing being acted upon." Policies that need a
sub-property (e.g., `request.Order` instead of the whole request) extract it inside the
policy body: `rc.Resource.Order.OwnerId`. No `IHasResource<T>` marker, no extractor delegate.

### 2. Always wrap — no opt-in

Every dispatch through `AuthorizationBehavior` constructs a `ResourceSecurityContextAdapter<TRequest>`
wrapping the resolved `ISecurityContext` plus the `request` value. Policies that don't care about
the resource cast `ctx` as `ISecurityContext` and ignore the rest; the `is IResourceSecurityContext<T> rc`
check inside the policy is the natural gate.

Cost per dispatch: one ~16 B class allocation (object header + 2 reference fields). Negligible
next to the DI resolution + ValueTask state machine cost already paid per dispatch. An opt-in
marker interface was considered (zero-alloc preservation for non-resource policies) but rejected:
the failure mode of forgetting the marker is worse than the 16 B cost, and an "all dispatches
get the resource" mental model is simpler than a per-request opt-in surface.

### 3. Adapter is `internal sealed class`

Not part of the public API. Pure runtime implementation detail of `AuthorizationBehavior`.
PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt see no changes.

## Design

### Adapter

New file `src/ZeroAlloc.Mediator.Authorization/ResourceSecurityContextAdapter.cs`:

```csharp
using System.Collections.Generic;
using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Per-dispatch adapter that exposes the dispatched request as the typed
/// <see cref="IResourceSecurityContext{TResource}.Resource"/> while forwarding all
/// <see cref="ISecurityContext"/> members to the inner caller-identity context.
/// </summary>
internal sealed class ResourceSecurityContextAdapter<TResource> : IResourceSecurityContext<TResource>
{
    private readonly ISecurityContext _inner;

    public ResourceSecurityContextAdapter(ISecurityContext inner, TResource resource)
    {
        _inner = inner;
        Resource = resource;
    }

    public TResource Resource { get; }

    // ISecurityContext forwarders.
    public string Id => _inner.Id;
    public IReadOnlySet<string> Roles => _inner.Roles;
    public IReadOnlyDictionary<string, string> Claims => _inner.Claims;
}
```

### `AuthorizationBehavior.Handle` change

In `src/ZeroAlloc.Mediator.Authorization/AuthorizationBehavior.cs`, immediately before the
`authorizer.EvaluateAsync(ctx, ct)` call, wrap `ctx`:

```diff
 var ctx = sp.GetService<ISecurityContext>();
 if (ctx is null)
 {
     throw new InvalidOperationException(
         "AuthorizationBehavior could not resolve ISecurityContext. ...");
 }

-var result = await authorizer.EvaluateAsync(ctx, ct).ConfigureAwait(false);
+var resourceCtx = new ResourceSecurityContextAdapter<TRequest>(ctx, request);
+var result = await authorizer.EvaluateAsync(resourceCtx, ct).ConfigureAwait(false);
```

Existing fail-open paths (`sp is null`, `authorizer is null`) are unchanged — they short-circuit
before the wrap.

### Consumer-side effect (no consumer changes required)

A resource-based policy that was previously dormant (`ctx is IResourceSecurityContext<...> rc`
always returned false) now resolves correctly when dispatched through Mediator:

```csharp
public sealed class OwnerOnlyDeletePolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx is IResourceSecurityContext<DeleteUserCommand> rc && rc.Resource.UserId == ctx.Id
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure("delete.not_owner"));
}

[Policy("OwnerOnlyDelete")]
public sealed class OwnerOnlyDeletePolicyRegistration { /* class-level [Policy] marker; impl in OwnerOnlyDeletePolicy */ }

[RequirePolicy("OwnerOnlyDelete")]
public sealed record DeleteUserCommand(string UserId) : IRequest<Unit>;
```

A policy that type-checks for a DIFFERENT request type correctly falls through:

```csharp
// Policy expects IResourceSecurityContext<Post>; dispatch is DeleteUserCommand.
// ctx is IResourceSecurityContext<Post> rc  →  false  →  policy returns failure.
```

This is the documented behaviour from Authorization v2.1's `resource-based-authorization.md` —
nothing new for the consumer; the adapter just makes the cast succeed when the types align.

## Tests

`tests/ZeroAlloc.Mediator.Authorization.Tests/ResourceSecurityContextTests.cs` (new):

- **`Policy_Sees_DispatchedRequest_AsResource`** — dispatch a `DeleteUserCommand("alice")` through `IMediator.Send`, with an authenticated security context whose `Id == "alice"`. Assert: handler runs (policy passed).
- **`Policy_TypeChecking_WrongRequestType_FallsThrough`** — a policy that type-checks `IResourceSecurityContext<UnrelatedCommand>` falls through to its failure branch when dispatched on a `DeleteUserCommand`. Assert: response is `AuthorizationDeniedException` (or `Result<,>.Failure(...)` if the request shape supports it).

`tests/ZeroAlloc.Mediator.Authorization.Tests/AllocationBudgetTests.cs` (modify):

- Existing `AuthorizationBehavior_Allow_Path_WithinBudget` test budget may need to widen by ~16 B to accommodate the adapter alloc. Measure first; document the new headroom in the test comment.

No snapshot tests — `Mediator.Authorization` has no generator emit of its own; the behaviour is plain runtime code.

## Out of scope

- **AI.Sentinel adoption.** Tracked separately. Same shape: AI.Sentinel's existing tool-call dispatch behaviour can adopt the same adapter pattern to expose the tool call as the resource.
- **Sub-property as resource (`IHasResource<TResource>`).** Considered, rejected as YAGNI. Sub-property access happens inside the policy body via `rc.Resource.X.Y`.
- **Opt-in / marker interface.** Considered, rejected — the 16 B per-dispatch cost is small enough that mandatory wrap is the better trade-off.
- **AOT compatibility audit.** The adapter is a sealed class with no reflection / no dynamic code. AOT-safe by construction; existing PackSmoke binary will continue to exercise `AuthorizationBehavior.Handle` and exercises the wrap path transitively.

## Files touched

Runtime (`src/ZeroAlloc.Mediator.Authorization/`):

- NEW: `ResourceSecurityContextAdapter.cs` (the adapter class).
- MOD: `AuthorizationBehavior.cs` (wrap `ctx` before passing to authorizer).

Tests (`tests/ZeroAlloc.Mediator.Authorization.Tests/`):

- NEW: `ResourceSecurityContextTests.cs` (2 runtime tests).
- MOD: `AllocationBudgetTests.cs` (raise the dispatch budget by ~16 B if measurement requires; document the headroom in a comment).

Docs (`docs/`):

- MOD or NEW: a short section / page on resource-based policies under Mediator.Authorization,
  linking out to `ZeroAlloc.Authorization`'s `resource-based-authorization.md`. Discover the existing convention before authoring.

## Backward compatibility

Strictly additive runtime behaviour. No PublicAPI change. No interface or signature change.
v2.0/v2.1 consumers of `Mediator.Authorization` that don't write resource-based policies see
no behaviour change beyond the ~16 B per-dispatch adapter allocation.

No SemVer break. Lands as a `feat:` commit; release-please bumps Mediator.Authorization minor
(per the package's conventional-commits config).
