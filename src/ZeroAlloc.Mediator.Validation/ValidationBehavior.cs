using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation;

// Order -750: after authorization, so an unauthorized caller learns nothing from validation
// errors, and before the cache, so an invalid request is never answered from it.
//
// Validators may be scoped, so they are resolved from PipelineBehaviorServices.Current: the
// provider of the IMediator dispatching the request, which is the request's scope when IMediator
// was resolved from one. A request dispatched without IMediator gets a scope of its own on the
// container WithValidation was wired to, and only when a validator is registered for it. A
// validator is never resolved from the root provider, which would share one instance of a scoped
// validator, and whatever it depends on, across every request.
[PipelineBehavior(Order = -750)]
public sealed class ValidationBehavior : IPipelineBehavior
{
    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var services = PipelineBehaviorServices.Current;
        return services is not null
            ? ValidateAsync(services, request, ct, next)
            : ValidateInOwnScopeAsync(request, ct, next);
    }

    private static async ValueTask<TResponse> ValidateInOwnScopeAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var root = ValidationBehaviorState.ServiceProvider;
        if (root is null)
            return await next(request, ct).ConfigureAwait(false);

        // No validator for this request type: skip opening a scope for it.
        // IServiceProviderIsService answers from the registrations without creating anything.
        if (root.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(ValidatorFor<TRequest>)))
            return await next(request, ct).ConfigureAwait(false);

        var scope = root.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await ValidateAsync(scope.ServiceProvider, request, ct, next).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResponse> ValidateAsync<TRequest, TResponse>(
        IServiceProvider services,
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var validator = services.GetService<ValidatorFor<TRequest>>();
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
