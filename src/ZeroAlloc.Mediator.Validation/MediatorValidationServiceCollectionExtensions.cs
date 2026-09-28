using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Validation;

public static class MediatorValidationServiceCollectionExtensions
{
    /// <summary>
    /// Hands the container to <see cref="ValidationBehavior"/> so it can resolve
    /// <c>ValidatorFor&lt;TRequest&gt;</c>. Idempotent — safe to call more than once.
    /// </summary>
    /// <remarks>
    /// The first <c>IMediator</c> resolved from the built container wires the behavior. An app
    /// that only dispatches through the static <c>Mediator</c> class resolves
    /// <see cref="PipelineBehaviorStateActivation"/> once instead.
    /// </remarks>
    public static IMediatorBuilder WithValidation(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithValidation more than once.
        if (services.Any(d => d.ServiceType == typeof(ValidationBehaviorAccessor)))
            return builder;

        services.AddSingleton(sp => new ValidationBehaviorAccessor(sp));
        services.AddSingleton<IPipelineBehaviorStateInitializer>(sp => sp.GetRequiredService<ValidationBehaviorAccessor>());

        return builder;
    }
}
