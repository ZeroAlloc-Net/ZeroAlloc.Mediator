using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation.Tests;

// -- Request / handler types --------------------------------------------------

public readonly record struct ValidatedRequest(string Name) : IRequest<Result<string, ValidationError>>;
public readonly record struct UnvalidatedRequest(int Value) : IRequest<int>;
public readonly record struct ThrowingRequest(string Name) : IRequest<string>;

// Manual ValidatorFor<T> — no generator needed in tests.
public sealed class ValidatedRequestValidator : ValidatorFor<ValidatedRequest>
{
    public override ValidationResult Validate(ValidatedRequest instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Name))
            return new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "must not be empty" }]);

        return new ValidationResult([]);
    }
}

public sealed class ThrowingRequestValidator : ValidatorFor<ThrowingRequest>
{
    public override ValidationResult Validate(ThrowingRequest instance)
    {
        if (string.IsNullOrWhiteSpace(instance.Name))
            return new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "must not be empty" }]);

        return new ValidationResult([]);
    }
}

// -- Tests --------------------------------------------------------------------

// Tests mutate the shared static ValidationBehaviorState.ServiceProvider.
[CollectionDefinition("non-parallel-validation", DisableParallelization = true)]
public sealed class NonParallelValidationCollection { }

[Collection("non-parallel-validation")]
public class ValidationBehaviorTests : IDisposable
{
    public ValidationBehaviorTests()
    {
        ValidationBehaviorState.ServiceProvider = null;
    }

    public void Dispose()
    {
        ValidationBehaviorState.ServiceProvider = null;
    }

    [Fact]
    public async Task NoValidatorRegistered_PassesThroughToNext()
    {
        var services = new ServiceCollection();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        var nextCalled = false;
        ValueTask<Result<string, ValidationError>> Next(ValidatedRequest r, CancellationToken c)
        {
            nextCalled = true;
            return ValueTask.FromResult(Result<string, ValidationError>.Success(r.Name));
        }

        var result = await ValidationBehavior.Handle(
            new ValidatedRequest("Alice"), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ValidatorRegistered_ValidationPasses_CallsNext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<ValidatedRequest>, ValidatedRequestValidator>();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        var nextCalled = false;
        ValueTask<Result<string, ValidationError>> Next(ValidatedRequest r, CancellationToken c)
        {
            nextCalled = true;
            return ValueTask.FromResult(Result<string, ValidationError>.Success(r.Name));
        }

        var result = await ValidationBehavior.Handle(
            new ValidatedRequest("Bob"), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.True(result.IsSuccess);
        Assert.Equal("Bob", result.Value);
    }

    [Fact]
    public async Task ValidatorRegistered_ValidationFails_ReturnsFailureResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<ValidatedRequest>, ValidatedRequestValidator>();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        var nextCalled = false;
        ValueTask<Result<string, ValidationError>> Next(ValidatedRequest r, CancellationToken c)
        {
            nextCalled = true;
            return ValueTask.FromResult(Result<string, ValidationError>.Success(r.Name));
        }

        var result = await ValidationBehavior.Handle(
            new ValidatedRequest(""), CancellationToken.None, Next);

        Assert.False(nextCalled);
        Assert.True(result.IsFailure);
        Assert.Single(result.Error.Failures);
        Assert.Equal("Name", result.Error.Failures[0].PropertyName);
    }

    [Fact]
    public async Task NoServiceProvider_PassesThroughToNext()
    {
        ValidationBehaviorState.ServiceProvider = null;

        var nextCalled = false;
        ValueTask<int> Next(UnvalidatedRequest r, CancellationToken c)
        {
            nextCalled = true;
            return ValueTask.FromResult(r.Value * 2);
        }

        var result = await ValidationBehavior.Handle(
            new UnvalidatedRequest(5), CancellationToken.None, Next);

        Assert.True(nextCalled);
        Assert.Equal(10, result);
    }

    [Fact]
    public void AddMediatorValidation_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddMediatorValidation();
        services.AddMediatorValidation(); // second call must be a no-op

        var count = services.Count(d => d.ServiceType == typeof(ValidationBehaviorAccessor));
        Assert.Equal(1, count); // idempotent — only one registration expected
    }

    [Fact]
    public async Task ValidatorRegistered_ValidationFails_NonResultResponse_ThrowsValidationFailedException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidatorFor<ThrowingRequest>, ThrowingRequestValidator>();
        ValidationBehaviorState.ServiceProvider = services.BuildServiceProvider();

        ValueTask<string> Next(ThrowingRequest r, CancellationToken c) =>
            ValueTask.FromResult(r.Name);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            ValidationBehavior.Handle(
                new ThrowingRequest(""), CancellationToken.None, Next).AsTask());

        Assert.Single(ex.Error.Failures);
        Assert.Equal("Name", ex.Error.Failures[0].PropertyName);
        Assert.Equal("must not be empty", ex.Error.Failures[0].ErrorMessage);
    }
}
