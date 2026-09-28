using Microsoft.Extensions.Caching.Memory;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Pipeline behavior that short-circuits with a cached response when the request type
/// carries <see cref="CacheResponseAttribute"/>. The Mediator source generator puts it in the
/// pipeline of every request once this package is referenced;
/// <see cref="MediatorCacheServiceCollectionExtensions.WithCache"/> supplies the cache it stores
/// responses in. Requests without the attribute pass through at the cost of one static field read
/// per TRequest type.
/// </summary>
// Order -500: after validation, so only valid requests reach the cache, and outside resilience,
// so a cache hit skips the retries.
[PipelineBehavior(Order = -500)]
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
                "CacheBehavior requires IMemoryCache. Call services.AddMediator().WithCache() at startup, then resolve " +
                "IMediator from the built container before the first cached request. An app that dispatches only " +
                "through the static Mediator class resolves PipelineBehaviorStateActivation instead. The state is " +
                "also cleared when the container that supplied the cache is disposed.");

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
