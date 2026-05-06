using System;
using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Thrown by the authorization pipeline behavior when a request marked with
/// <see cref="ZeroAlloc.Authorization.AuthorizeAttribute"/> is denied and the request
/// shape is plain <see cref="ZeroAlloc.Mediator.IRequest{TResponse}"/> (i.e. NOT an
/// <see cref="IAuthorizedRequest{TResponse}"/>). For Result-shaped opt-in via
/// <see cref="IAuthorizedRequest{TResponse}"/>, the behavior returns a failure
/// <see cref="ZeroAlloc.Results.Result{T,E}"/> instead of throwing.
/// </summary>
// RCS1194: AuthorizationDeniedException intentionally omits parameter-free constructors — an AuthorizationFailure is always required.
#pragma warning disable RCS1194
public sealed class AuthorizationDeniedException : Exception
{
    public AuthorizationFailure Failure { get; }

    public AuthorizationDeniedException(AuthorizationFailure failure)
        : base($"Authorization denied: {failure.Code}")
    {
        Failure = failure;
    }
}
#pragma warning restore RCS1194
