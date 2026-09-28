using Microsoft.Extensions.Caching.Memory;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Singleton that bridges the container's <see cref="IMemoryCache"/> into the static
/// <see cref="CacheBehavior"/> pipeline step. The first <c>IMediator</c> resolved from the
/// container runs <see cref="Initialize"/> through <see cref="PipelineBehaviorStateActivation"/>,
/// which points <see cref="CacheBehaviorState"/> at this container's cache.
/// </summary>
/// <remarks>
/// The container owns the cache and disposes it with itself, and it disposes this accessor at the
/// same time. <see cref="Dispose"/> then clears the static state, but only while it still holds
/// this container's cache, so disposing one container never unhooks another one that is live.
/// </remarks>
internal sealed class MediatorCacheAccessor : IPipelineBehaviorStateInitializer, IDisposable
{
    private readonly IMemoryCache _cache;

    internal MediatorCacheAccessor(IMemoryCache cache) => _cache = cache;

    public void Initialize() => CacheBehaviorState.SetCache(_cache);

    public void Dispose() => CacheBehaviorState.ClearCache(_cache);
}
