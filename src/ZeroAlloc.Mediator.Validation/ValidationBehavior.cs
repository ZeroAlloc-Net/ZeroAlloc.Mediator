using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation;

[PipelineBehavior]
public sealed class ValidationBehavior : IPipelineBehavior
{
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
