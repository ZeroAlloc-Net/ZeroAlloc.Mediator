using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Validation;

// Per-TResponse static cache. Populated once via reflection when TResponse is
// Result<TValue, ValidationError> — zero-allocation on subsequent calls.
//
// The [DynamicallyAccessedMembers(PublicMethods)] annotation tells the trimmer to preserve TResponse's
// public methods — specifically Result<,>.Failure(...) which we look up reflectively. Without this
// annotation, dotnet publish -r <rid> emits IL2090. Same precedent as MediatorBuilderExtensions.cs:77.
internal static class FailureFactory<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TResponse>
{
    internal static readonly Func<ValidationError, TResponse>? Create = BuildFactory();

    private static Func<ValidationError, TResponse>? BuildFactory()
    {
        var responseType = typeof(TResponse);
        if (!responseType.IsGenericType)
            return null;

        var def = responseType.GetGenericTypeDefinition();
        if (def != typeof(Result<,>))
            return null;

        var typeArgs = responseType.GetGenericArguments();
        // typeArgs[1] must be ValidationError
        if (typeArgs[1] != typeof(ValidationError))
            return null;

        var method = responseType.GetMethod(
            nameof(Result<object, ValidationError>.Failure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(ValidationError)]);
        if (method is null) return null;

        return method.CreateDelegate<Func<ValidationError, TResponse>>();
    }
}
