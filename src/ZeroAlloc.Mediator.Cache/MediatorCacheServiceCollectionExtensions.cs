using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Cache;

public static class MediatorCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMemoryCache"/> and hands it to <see cref="CacheBehavior"/>.
    /// Idempotent — safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <see cref="CacheBehavior"/> is static, so it reads the cache from static state. The first
    /// <c>IMediator</c> resolved from the built container fills that state with the container's
    /// own <see cref="IMemoryCache"/>. An app that only dispatches through the static
    /// <c>Mediator</c> class resolves <see cref="PipelineBehaviorStateActivation"/> once instead.
    /// </remarks>
    public static IMediatorBuilder WithCache(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithCache more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorCacheAccessor)))
            return builder;

        services.AddMemoryCache();

        // Register using a factory so DI doesn't require a public constructor.
        services.AddSingleton(sp => new MediatorCacheAccessor(sp.GetRequiredService<IMemoryCache>()));
        services.AddSingleton<IPipelineBehaviorStateInitializer>(sp => sp.GetRequiredService<MediatorCacheAccessor>());

        return builder;
    }
}
