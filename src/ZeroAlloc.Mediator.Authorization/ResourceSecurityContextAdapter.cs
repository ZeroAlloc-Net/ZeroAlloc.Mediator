using System.Collections.Generic;
using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Per-dispatch adapter that exposes the dispatched request as the typed
/// <see cref="IResourceSecurityContext{TResource}.Resource"/> while forwarding all
/// <see cref="ISecurityContext"/> members to the inner caller-identity context.
/// </summary>
/// <remarks>
/// <para>Constructed inside <see cref="AuthorizationBehavior.Handle{TRequest, TResponse}"/>
/// once per dispatch. Cost is one ~16 B class allocation (object header + 2 reference
/// fields) — small enough to be absorbed by <see cref="AuthorizationBehavior"/>'s existing
/// allocation budget; see <c>AllocationBudgetTests</c>.</para>
/// <para>Internal-only by design: consumer policies type-check the public
/// <see cref="IResourceSecurityContext{TResource}"/> interface, never the adapter type.</para>
/// </remarks>
internal sealed class ResourceSecurityContextAdapter<TResource> : IResourceSecurityContext<TResource>
{
    private readonly ISecurityContext _inner;

    public ResourceSecurityContextAdapter(ISecurityContext inner, TResource resource)
    {
        _inner = inner;
        Resource = resource;
    }

    public TResource Resource { get; }

    public string Id => _inner.Id;
    public IReadOnlySet<string> Roles => _inner.Roles;
    public IReadOnlyDictionary<string, string> Claims => _inner.Claims;
}
