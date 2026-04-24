using System.Linq;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation;

public readonly struct ValidationError
{
    public ValidationError(ValidationFailure[] failures)
    {
        Failures = failures;
    }

    public ValidationFailure[] Failures { get; }

    public override string ToString() =>
        Failures is { Length: > 0 }
            ? string.Join("; ", Failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"))
            : "Validation failed.";
}
