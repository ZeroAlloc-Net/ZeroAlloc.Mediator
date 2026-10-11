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
/// once per dispatch. Cost is one class allocation: the 16 B object header and method table,
/// 8 B for the inner context, and the request stored in <see cref="Resource"/>, rounded up to
/// 8 B. That is 32 B for a reference-type request or a value-type request of up to 8 B, and
/// more for a larger struct; see <c>AllocationBudgetTests</c>.</para>
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
