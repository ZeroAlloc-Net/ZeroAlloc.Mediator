using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation;

// Order -750: after authorization, so an unauthorized caller learns nothing from validation
// errors, and before the cache, so an invalid request is never answered from it.
//
// Validators may be scoped, so they are resolved from the provider of the IMediator dispatching
// the request, which is the request's scope when IMediator was resolved from one. The generated
// IMediator.Send passes that provider to Handle as its state. A request dispatched without
// IMediator, whose state is null, gets a scope of its own on the
// container WithValidation was wired to, and only when a validator is registered for it. A
// validator is never resolved from the root provider, which would share one instance of a scoped
// validator, and whatever it depends on, across every request.
//
// Pipeline behaviors do not wrap CreateStream, so a stream request is validated by CheckStream,
// which the generated CreateStream calls before the stream handler.
[PipelineBehavior(Order = -750)]
public sealed class ValidationBehavior : IPipelineBehavior
{
    /// <summary>
    /// Validates <paramref name="request"/> with the services of the request being dispatched.
    /// The generated <c>IMediator.Send</c> passes the provider it was resolved from as
    /// <paramref name="state"/>, so the pipeline needs no ambient state and allocates no delegate.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The pipeline state, the provider of the request being dispatched.</typeparam>
    /// <param name="request">The request to validate.</param>
    /// <param name="state">
    /// The provider of the request being dispatched. The validator is resolved from it.
    /// <see langword="null"/> for a request dispatched through the static <c>Mediator</c> class,
    /// which has no request scope: the behavior then opens a scope of its own.
    /// </param>
    /// <param name="ct">The cancellation token of the request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        where TState : IServiceProvider?
        => state is null
            ? ValidateInOwnScopeAsync(request, state, ct, next)
            : ValidateAsync(state, request, state, ct, next);

    /// <summary>
    /// Validates a stream request before its handler runs. The generated <c>CreateStream</c>
    /// calls this when enumeration starts, after authorization, and an invalid request throws
    /// <see cref="ValidationFailedException"/> before the handler is called and before any item
    /// is yielded. Pipeline behaviors do not wrap streams, so this is how validation applies to
    /// a type that implements <c>IStreamRequest&lt;T&gt;</c>.
    /// </summary>
    /// <typeparam name="TRequest">The stream request type.</typeparam>
    /// <param name="request">The stream request to validate.</param>
    /// <param name="services">
    /// The provider of the request being dispatched, which the generated <c>IMediator</c> passes.
    /// When <see langword="null"/>, as from the static <c>Mediator</c> class, the check runs in a
    /// scope of its own, as for <c>Send</c>.
    /// </param>
    /// <param name="ct">The cancellation token of the enumeration.</param>
    /// <returns>A task that completes when the request is valid.</returns>
    /// <exception cref="ValidationFailedException">The request failed validation.</exception>
    public static ValueTask CheckStream<TRequest>(TRequest request, IServiceProvider? services, CancellationToken ct)
    {
        return services is not null
            ? ValidateStreamAsync(services, request, ct)
            : ValidateStreamInOwnScopeAsync(request, ct);
    }

    private static async ValueTask ValidateStreamInOwnScopeAsync<TRequest>(TRequest request, CancellationToken ct)
    {
        var root = ValidationBehaviorState.ServiceProvider;
        if (root is null)
            return;

        if (root.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(ValidatorFor<TRequest>)))
            return;

        var scope = root.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await ValidateStreamAsync(scope.ServiceProvider, request, ct).ConfigureAwait(false);
        }
    }

    // A stream has no response to carry a failure, so an invalid request always throws
    // ValidationFailedException, as a plain IRequest does.
    private static async ValueTask ValidateStreamAsync<TRequest>(
        IServiceProvider services,
        TRequest request,
        CancellationToken ct)
    {
        var validator = services.GetService<ValidatorFor<TRequest>>();
        if (validator is null)
            return;

        var validationResult = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validationResult.IsValid)
            throw new ValidationFailedException(new ValidationError(validationResult.Failures.ToArray()));
    }

    private static async ValueTask<TResponse> ValidateInOwnScopeAsync<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var root = ValidationBehaviorState.ServiceProvider;
        if (root is null)
            return await next(request, state, ct).ConfigureAwait(false);

        // No validator for this request type: skip opening a scope for it.
        // IServiceProviderIsService answers from the registrations without creating anything.
        if (root.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(ValidatorFor<TRequest>)))
            return await next(request, state, ct).ConfigureAwait(false);

        var scope = root.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await ValidateAsync(scope.ServiceProvider, request, state, ct, next).ConfigureAwait(false);
        }
    }

    private static async ValueTask<TResponse> ValidateAsync<TRequest, TResponse, TState>(
        IServiceProvider services,
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var validator = services.GetService<ValidatorFor<TRequest>>();
        if (validator is null)
            return await next(request, state, ct).ConfigureAwait(false);

        var validationResult = await validator.ValidateAsync(request, ct).ConfigureAwait(false);
        if (!validationResult.IsValid)
        {
            var factory = FailureFactory<TResponse>.Create;
            if (factory is null)
                throw new ValidationFailedException(new ValidationError(validationResult.Failures.ToArray()));

            return factory(new ValidationError(validationResult.Failures.ToArray()));
        }

        return await next(request, state, ct).ConfigureAwait(false);
    }
}
