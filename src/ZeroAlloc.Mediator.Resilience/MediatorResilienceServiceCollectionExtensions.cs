using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Resilience;

public static class MediatorResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the resilience pipeline-behavior marker.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IMediatorBuilder WithResilience(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithResilience more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorResilienceMarker)))
            return builder;

        services.AddSingleton<MediatorResilienceMarker>();

        return builder;
    }
}
