using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Cache;

public static class MediatorCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMemoryCache"/> and the cache pipeline-behavior accessor.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IMediatorBuilder WithCache(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithCache more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorCacheAccessor)))
            return builder;

        services.AddMemoryCache();

        // Register using a factory so DI doesn't require a public constructor.
        services.AddSingleton(sp => new MediatorCacheAccessor(sp.GetRequiredService<IMemoryCache>()));

        // Eagerly resolve MediatorCacheAccessor so CacheBehaviorState.Cache is populated
        // before the first cached request arrives, without requiring the consumer to pull an
        // internal type from their container manually.
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<MediatorCacheAccessor>();

        return builder;
    }
}
