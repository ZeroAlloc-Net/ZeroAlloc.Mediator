using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Validation;

// Per-TResponse static cache of the failure constructor. Result<T, ValidationError> and
// UnitResult<ValidationError> implement IFailureFactory, so this needs no reflection and stays
// trimming and Native AOT safe. The value-type default boxes once, here, never per request.
// Any other TResponse leaves Create null, and the behaviour throws ValidationFailedException.
internal static class FailureFactory<TResponse>
{
    internal static readonly Func<ValidationError, TResponse>? Create =
        default(TResponse) is IFailureFactory<TResponse, ValidationError> factory
            ? factory.CreateFailure
            : null;
}
