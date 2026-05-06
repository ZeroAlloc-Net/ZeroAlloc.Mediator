using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation;

[PipelineBehavior]
public sealed class ValidationBehavior : IPipelineBehavior
{
    // IL2091: FailureFactory<TResponse> requires TResponse to satisfy
    // [DynamicallyAccessedMembers(PublicMethods)] so the trimmer preserves Result<,>.Failure(...).
    // Handle's TResponse can't carry that annotation without forcing every consumer of IPipelineBehavior
    // to declare it. Safe to suppress because FailureFactory does a runtime shape check
    // (GenericTypeDefinition == typeof(Result<,>) / typeArgs[1] == typeof(ValidationError)) and only
    // proceeds when TResponse is exactly Result<T, ValidationError>; otherwise Create is null and we
    // fall through to the throw path that doesn't touch TResponse reflectively. Result<,> lives in
    // ZeroAlloc.Results, referenced by this assembly, so its public methods are preserved.
    [UnconditionalSuppressMessage("Trimming", "IL2091:Target generic argument does not satisfy DynamicallyAccessedMemberTypes",
        Justification = "FailureFactory does a runtime shape check; Result<,> is in a referenced assembly and its Failure method is preserved by trimming roots.")]
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var sp = ValidationBehaviorState.ServiceProvider;
        if (sp is null)
            return await next(request, ct).ConfigureAwait(false);

        var validator = sp.GetService<ValidatorFor<TRequest>>();
        if (validator is null)
            return await next(request, ct).ConfigureAwait(false);

        var validationResult = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            var factory = FailureFactory<TResponse>.Create;
            if (factory is null)
                throw new ValidationFailedException(new ValidationError(validationResult.Failures.ToArray()));

            return factory(new ValidationError(validationResult.Failures.ToArray()));
        }

        return await next(request, ct).ConfigureAwait(false);
    }
}
