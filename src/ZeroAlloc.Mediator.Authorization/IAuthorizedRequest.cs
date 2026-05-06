using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>Marker interface — opt into the Result-shaped deny path for an authorized request.</summary>
/// <remarks>
/// Plain <see cref="IRequest{TResponse}"/> + <see cref="ZeroAlloc.Authorization.AuthorizeAttribute"/>
/// causes the behavior to throw <see cref="AuthorizationDeniedException"/> on deny.
/// Replacing <c>IRequest&lt;T&gt;</c> with <c>IAuthorizedRequest&lt;T&gt;</c> changes the
/// emission shape: the behavior returns
/// <c>Result&lt;T, <see cref="AuthorizationFailure"/>&gt;.Failure(...)</c> on deny instead.
/// </remarks>
public interface IAuthorizedRequest<TResponse>
    : IRequest<Result<TResponse, AuthorizationFailure>>
{
}
