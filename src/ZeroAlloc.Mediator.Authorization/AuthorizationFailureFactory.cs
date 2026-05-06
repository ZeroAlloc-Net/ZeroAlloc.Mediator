using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization;

// Per-TResponse cache: builds a delegate that constructs Result<TValue, AuthorizationFailure>.Failure(...)
// when TResponse has that shape; null otherwise (throw path). Mirrors ValidationBehavior's FailureFactory.
//
// The [DynamicallyAccessedMembers(PublicMethods)] annotation tells the trimmer to preserve TResponse's
// public methods — specifically Result<,>.Failure(...) which we look up reflectively. Without this
// annotation, dotnet publish -r <rid> emits IL2090. Same precedent as MediatorBuilderExtensions.cs:77.
internal static class AuthorizationFailureFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>
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
