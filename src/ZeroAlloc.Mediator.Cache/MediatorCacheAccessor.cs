using Microsoft.Extensions.Caching.Memory;

namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Singleton that bridges DI-managed <see cref="IMemoryCache"/> into the static
/// <see cref="CacheBehavior"/> pipeline step. Resolving this singleton (which happens
/// lazily on the first request that needs a cached response, or eagerly if the app
/// resolves it explicitly) wires the cache into <see cref="CacheBehaviorState"/>.
/// </summary>
internal sealed class MediatorCacheAccessor
{
    internal MediatorCacheAccessor(IMemoryCache cache)
    {
        CacheBehaviorState.SetCache(cache);
    }
}
