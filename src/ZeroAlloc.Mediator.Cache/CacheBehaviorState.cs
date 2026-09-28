using Microsoft.Extensions.Caching.Memory;

namespace ZeroAlloc.Mediator.Cache;

// Holds the container's IMemoryCache for the static CacheBehavior. MediatorCacheAccessor sets it
// when the container first resolves IMediator, and clears it when the container is disposed.
// volatile ensures writes are visible across threads without reordering on weakly-ordered architectures.
internal static class CacheBehaviorState
{
    internal static volatile IMemoryCache? Cache;

    internal static void SetCache(IMemoryCache cache)
    {
        Cache = cache;
    }

    // Clears the state only while it still holds this cache. Another container may have taken
    // over since, and its cache must stay in place.
    internal static void ClearCache(IMemoryCache cache)
    {
        Interlocked.CompareExchange(ref Cache, null, cache);
    }
}
