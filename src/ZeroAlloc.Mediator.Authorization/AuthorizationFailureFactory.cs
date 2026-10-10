using System;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

// Per-TResponse static cache of the failure constructor. Result<T, AuthorizationFailure> and
// UnitResult<AuthorizationFailure> implement IFailureFactory, so this needs no reflection and stays
// trimming and Native AOT safe. The value-type default boxes once, here, never per request.
// Any other TResponse leaves Create null, and the behaviour throws AuthorizationDeniedException.
internal static class AuthorizationFailureFactory<TResponse>
{
    internal static readonly Func<AuthorizationFailure, TResponse>? Create =
        default(TResponse) is IFailureFactory<TResponse, AuthorizationFailure> factory
            ? factory.CreateFailure
            : null;
}
