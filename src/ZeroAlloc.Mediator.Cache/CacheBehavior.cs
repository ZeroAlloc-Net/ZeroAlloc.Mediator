using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// Answers <paramref name="request"/> from the cache of the container dispatching it. The
    /// generated <c>IMediator.Send</c> passes the provider it was resolved from as
    /// <paramref name="state"/>, so the pipeline needs no ambient state and allocates no delegate.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The pipeline state, the provider of the request being dispatched.</typeparam>
    /// <param name="request">The request to answer.</param>
    /// <param name="state">
    /// The provider of the request being dispatched. The cache is resolved from it.
    /// <see langword="null"/> for a request dispatched through the static <c>Mediator</c> class,
    /// which then uses the cache of the container <c>WithCache</c> was wired to.
    /// </param>
    /// <param name="ct">The cancellation token of the request.</param>
    /// <param name="next">The rest of the pipeline.</param>
    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request,
        TState state,
        CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        where TState : IServiceProvider?
    {
        var attr = CacheAttributeCache<TRequest>.Attribute;
        if (attr is null)
            return await next(request, state, ct).ConfigureAwait(false);

        var cache = ResolveCache(state)
            ?? throw new InvalidOperationException(
                "CacheBehavior requires IMemoryCache. Call services.AddMediator().WithCache() at startup on the " +
                "container IMediator is resolved from. An app that dispatches only through the static Mediator class " +
                "resolves PipelineBehaviorStateActivation from the built container before the first cached request; " +
                "that state is cleared when the container that supplied the cache is disposed.");

        if (TryGetCached(cache, request, out TResponse? cached, out var key))
            return cached!;

        // Built before next runs, as the lookup was, so a handler that changes a class request
        // does not change the key its response is stored under.
        key ??= BuildKey(request);
        var result = await next(request, state, ct).ConfigureAwait(false);

        if (attr.Sliding)
            cache.Set(key, result, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMilliseconds(attr.TtlMs)
            });
        else
            cache.Set(key, result, TimeSpan.FromMilliseconds(attr.TtlMs));

        return result;
    }

    // The entry key: "<request type full name>:<request>", formatted with the current culture. An
    // app that removes or replaces an entry does so by this string, so it must not change.
    private static string BuildKey<TRequest>(TRequest request)
        => $"{CacheKeyPrefix<TRequest>.Value}:{request}";

    // A hit must not build the key string. On a MemoryCache the key is
    // formatted into a stack buffer by the same interpolation as BuildKey, so with the same culture
    // and the same ToString or ISpanFormattable.TryFormat call, and looked up as a span, which
    // finds the entry stored under the equal string. A request that implements ISpanFormattable
    // then costs nothing on a hit; any other request still pays for its ToString. A key longer
    // than the buffer, or any other IMemoryCache, is looked up by the string, which is handed
    // back in key so that a miss does not build it twice.
    //
    // The found entry is tested the way CacheExtensions.TryGetValue<TItem> tests it, so a stored
    // null is a hit and an entry of another type is a miss, as before.
    private static bool TryGetCached<TRequest, TResponse>(
        IMemoryCache cache, TRequest request, out TResponse? value, out string? key)
    {
        if (!TryGetEntry(cache, request, out var entry, out key))
        {
            value = default;
            return false;
        }

        if (entry is null)
        {
            value = default;
            return true;
        }

        if (entry is TResponse response)
        {
            value = response;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetEntry<TRequest>(IMemoryCache cache, TRequest request, out object? entry, out string? key)
    {
        if (cache is MemoryCache memoryCache)
        {
            Span<char> buffer = stackalloc char[KeyBufferLength];
            if (buffer.TryWrite($"{CacheKeyPrefix<TRequest>.Value}:{request}", out var length))
            {
                key = null;
                return memoryCache.TryGetValue((ReadOnlySpan<char>)buffer[..length], out entry);
            }
        }
        key = BuildKey(request);
        return cache.TryGetValue(key, out entry);
    }

    private const int KeyBufferLength = 256;

    // The cache of the container whose IMediator dispatches the request, so two containers in one
    // process never answer each other's requests. A request dispatched without IMediator, through
    // the static Mediator class, has no provider and uses the cache
    // PipelineBehaviorStateActivation handed over.
    private static IMemoryCache? ResolveCache(IServiceProvider? services)
        => services is not null
            ? services.GetService<MediatorCacheAccessor>()?.Cache
            : CacheBehaviorState.Cache;
}
