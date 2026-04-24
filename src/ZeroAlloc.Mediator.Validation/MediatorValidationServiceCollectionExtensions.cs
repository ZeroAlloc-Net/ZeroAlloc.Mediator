using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Validation;

public static class MediatorValidationServiceCollectionExtensions
{
    public static IServiceCollection AddMediatorValidation(this IServiceCollection services)
    {
        // Idempotency guard — safe to call AddMediatorValidation more than once.
        if (services.Any(d => d.ServiceType == typeof(ValidationBehaviorAccessor)))
            return services;

        services.AddSingleton(sp => new ValidationBehaviorAccessor(sp));

        return services;
    }
}
