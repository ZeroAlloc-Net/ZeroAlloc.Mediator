using System;
using System.Reflection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

// Per-TResponse cache: builds a delegate that constructs Result<TValue, AuthorizationFailure>.Failure(...)
// when TResponse has that shape; null otherwise (throw path). Mirrors ValidationBehavior's FailureFactory.
internal static class AuthorizationFailureFactory<TResponse>
{
    internal static readonly Func<AuthorizationFailure, TResponse>? Create = BuildFactory();

    private static Func<AuthorizationFailure, TResponse>? BuildFactory()
    {
        var responseType = typeof(TResponse);
        if (!responseType.IsGenericType) return null;

        var def = responseType.GetGenericTypeDefinition();
        if (def != typeof(Result<,>)) return null;

        var typeArgs = responseType.GetGenericArguments();
        if (typeArgs[1] != typeof(AuthorizationFailure)) return null;

        var method = responseType.GetMethod(
            nameof(Result<object, AuthorizationFailure>.Failure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(AuthorizationFailure)]);
        if (method is null) return null;

        return method.CreateDelegate<Func<AuthorizationFailure, TResponse>>();
    }
}
