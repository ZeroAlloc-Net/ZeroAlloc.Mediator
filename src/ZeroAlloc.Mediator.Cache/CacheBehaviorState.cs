using Microsoft.Extensions.Caching.Memory;

namespace ZeroAlloc.Mediator.Cache;

// Holds the resolved IMemoryCache. Populated by MediatorCacheAccessor on first DI resolution.
// volatile ensures writes are visible across threads without reordering on weakly-ordered architectures.
internal static class CacheBehaviorState
{
    internal static volatile IMemoryCache? Cache;

    internal static void SetCache(IMemoryCache cache)
    {
        Cache = cache;
    }
}
