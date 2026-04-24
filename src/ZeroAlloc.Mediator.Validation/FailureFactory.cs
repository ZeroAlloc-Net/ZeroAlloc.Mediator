using System.Reflection;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Validation;

// Per-TResponse static cache. Populated once via reflection when TResponse is
// Result<TValue, ValidationError> — zero-allocation on subsequent calls.
internal static class FailureFactory<TResponse>
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
