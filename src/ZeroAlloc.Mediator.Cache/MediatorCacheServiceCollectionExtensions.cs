using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Cache;

public static class MediatorCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMemoryCache"/> and wires it into <see cref="CacheBehavior"/>.
    /// The cache is resolved from the container on the first cached request and pinned in a
    /// static field — no per-call DI lookup after that.
    /// </summary>
    public static IServiceCollection AddMediatorCache(this IServiceCollection services)
    {
        // Idempotency guard — safe to call AddMediatorCache more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorCacheAccessor)))
            return services;

        services.AddMemoryCache();

        // Register using a factory so DI doesn't require a public constructor.
        services.AddSingleton(sp => new MediatorCacheAccessor(sp.GetRequiredService<IMemoryCache>()));

        // Eagerly resolve MediatorCacheAccessor so CacheBehaviorState.Cache is populated
        // before the first cached request arrives, without requiring the consumer to pull an
        // internal type from their container manually.
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<MediatorCacheAccessor>();

        return services;
    }
}
