using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Validation;

public static class MediatorValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the validation pipeline-behavior accessor.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IMediatorBuilder WithValidation(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithValidation more than once.
        if (services.Any(d => d.ServiceType == typeof(ValidationBehaviorAccessor)))
            return builder;

        services.AddSingleton(sp => new ValidationBehaviorAccessor(sp));

        return builder;
    }
}
