using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.AotSmoke.Validation;

#pragma warning disable MA0048
public sealed record AotValidatedPayload(string Name);
public readonly record struct AotValidatedValue(string Name);

public sealed record AotValidateInt(string Name) : IRequest<Result<int, ValidationError>>;
public sealed record AotValidateClass(string Name) : IRequest<Result<AotValidatedPayload, ValidationError>>;
public sealed record AotValidateStruct(string Name) : IRequest<Result<AotValidatedValue, ValidationError>>;
public sealed record AotValidateUnit(string Name) : IRequest<UnitResult<ValidationError>>;

// Stub handlers — required by ZAM001 (every IRequest<T> needs a registered handler in the
// compilation). The scenario drives ValidationBehavior.Handle directly, so these never run.
public sealed class AotValidateIntHandler : IRequestHandler<AotValidateInt, Result<int, ValidationError>>
{
    public ValueTask<Result<int, ValidationError>> Handle(AotValidateInt r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, ValidationError>>(r.Name.Length);
}
public sealed class AotValidateClassHandler : IRequestHandler<AotValidateClass, Result<AotValidatedPayload, ValidationError>>
{
    public ValueTask<Result<AotValidatedPayload, ValidationError>> Handle(AotValidateClass r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotValidatedPayload, ValidationError>>(new AotValidatedPayload(r.Name));
}
public sealed class AotValidateStructHandler : IRequestHandler<AotValidateStruct, Result<AotValidatedValue, ValidationError>>
{
    public ValueTask<Result<AotValidatedValue, ValidationError>> Handle(AotValidateStruct r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotValidatedValue, ValidationError>>(new AotValidatedValue(r.Name));
}
public sealed class AotValidateUnitHandler : IRequestHandler<AotValidateUnit, UnitResult<ValidationError>>
{
    public ValueTask<UnitResult<ValidationError>> Handle(AotValidateUnit r, CancellationToken ct)
        => ValueTask.FromResult(UnitResult<ValidationError>.Success());
}

// Hand-written validators: each fails when Name is empty.
public sealed class AotValidateIntValidator : ValidatorFor<AotValidateInt>
{
    public override ValidationResult Validate(AotValidateInt instance) => AotValidation.CheckName(instance.Name);
}
public sealed class AotValidateClassValidator : ValidatorFor<AotValidateClass>
{
    public override ValidationResult Validate(AotValidateClass instance) => AotValidation.CheckName(instance.Name);
}
public sealed class AotValidateStructValidator : ValidatorFor<AotValidateStruct>
{
    public override ValidationResult Validate(AotValidateStruct instance) => AotValidation.CheckName(instance.Name);
}
public sealed class AotValidateUnitValidator : ValidatorFor<AotValidateUnit>
{
    public override ValidationResult Validate(AotValidateUnit instance) => AotValidation.CheckName(instance.Name);
}

internal static class AotValidation
{
    internal static ValidationResult CheckName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "must not be empty" }])
            : new ValidationResult([]);
}
#pragma warning restore MA0048

internal static class ValidatedScenario
{
    public static void Run()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<AotValidateInt>, AotValidateIntValidator>();
        services.AddSingleton<ValidatorFor<AotValidateClass>, AotValidateClassValidator>();
        services.AddSingleton<ValidatorFor<AotValidateStruct>, AotValidateStructValidator>();
        services.AddSingleton<ValidatorFor<AotValidateUnit>, AotValidateUnitValidator>();
        services.AddMediator().WithValidation();

        // Disposing the provider clears ValidationBehaviorState, so later scenarios are unaffected.
        using var sp = services.BuildServiceProvider();
        // Resolving IMediator wires ValidationBehaviorState to this container.
        _ = sp.GetRequiredService<IMediator>();

        VerifyFailure("int", new AotValidateInt(""),
            static (Result<int, ValidationError> r) => r.IsFailure ? r.Error : null);
        VerifyFailure("class", new AotValidateClass(""),
            static (Result<AotValidatedPayload, ValidationError> r) => r.IsFailure ? r.Error : null);
        VerifyFailure("struct", new AotValidateStruct(""),
            static (Result<AotValidatedValue, ValidationError> r) => r.IsFailure ? r.Error : null);
        VerifyFailure("unit", new AotValidateUnit(""),
            static (UnitResult<ValidationError> r) => r.IsFailure ? r.Error : null);

        Console.WriteLine("Mediator.Validation: failure results OK");
    }

    private static void VerifyFailure<TRequest, TResponse>(
        string name, TRequest request, Func<TResponse, ValidationError?> error)
        where TRequest : IRequest<TResponse>
    {
        var response = ValidationBehavior.Handle<TRequest, TResponse>(
            request, CancellationToken.None,
            static (_, _) => throw new InvalidOperationException("next must not run"))
            .GetAwaiter().GetResult();
        var failures = error(response)?.Failures;
        if (failures is not { Length: 1 } || !string.Equals(failures[0].PropertyName, "Name", StringComparison.Ordinal))
            throw new InvalidOperationException($"{name}-validation did not return a Name failure");
    }
}
