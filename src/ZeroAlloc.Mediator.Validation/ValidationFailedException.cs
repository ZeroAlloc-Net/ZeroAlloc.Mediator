namespace ZeroAlloc.Mediator.Validation;

// RCS1194: ValidationFailedException intentionally omits parameter-free constructors — a ValidationError is always required.
#pragma warning disable RCS1194
public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(ValidationError error)
        : base(error.ToString()) => Error = error;

    public ValidationFailedException(ValidationError error, string message)
        : base(message) => Error = error;

    public ValidationFailedException(ValidationError error, string message, Exception inner)
        : base(message, inner) => Error = error;

    public ValidationError Error { get; }
}
#pragma warning restore RCS1194
