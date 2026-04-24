using Microsoft.Extensions.Caching.Memory;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Pipeline behavior that short-circuits with a cached response when the request type
/// carries <see cref="CacheResponseAttribute"/>. Register globally via
/// <see cref="MediatorCacheServiceCollectionExtensions.AddMediatorCache"/>;
/// requests without the attribute pass through at the cost of one static field read per TRequest type.
/// </summary>
[PipelineBehavior]
public sealed class CacheBehavior : IPipelineBehavior
{
    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        var attr = CacheAttributeCache<TRequest>.Attribute;
        if (attr is null)
            return await next(request, ct).ConfigureAwait(false);

        var cache = CacheBehaviorState.Cache
            ?? throw new InvalidOperationException(
                "CacheBehavior requires IMemoryCache. Call services.AddMediatorCache() at startup and ensure " +
                "MediatorCacheAccessor is resolved before the first cached request.");

        var key = $"{typeof(TRequest).FullName ?? typeof(TRequest).Name}:{request}";

        if (cache.TryGetValue(key, out TResponse? cached))
            return cached!;

        var result = await next(request, ct).ConfigureAwait(false);

        if (attr.Sliding)
            cache.Set(key, result, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMilliseconds(attr.TtlMs)
            });
        else
            cache.Set(key, result, TimeSpan.FromMilliseconds(attr.TtlMs));

        return result;
    }
}
