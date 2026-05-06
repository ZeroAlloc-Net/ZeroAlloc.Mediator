using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Resolves the ambient <see cref="ISecurityContext"/> for the current logical operation
/// (HTTP request, message envelope, scheduled job). Hosts implement this and register it
/// in DI; opt in via <see cref="AuthorizationOptions.UseAccessor{TAccessor}"/>.
/// </summary>
public interface ISecurityContextAccessor
{
    ISecurityContext Current { get; }
}
