using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Resilience;

public static class MediatorResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddMediatorResilience(this IServiceCollection services)
    {
        // Idempotency guard — safe to call AddMediatorResilience more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorResilienceMarker)))
            return services;

        services.AddSingleton<MediatorResilienceMarker>();

        return services;
    }
}
