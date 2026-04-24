using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Cache;

namespace ZeroAlloc.Mediator.Cache.Tests;

// Disable parallelism for this collection — tests mutate the shared static CacheBehaviorState.Cache.
[CollectionDefinition("non-parallel", DisableParallelization = true)]
public sealed class NonParallelCollection { }

// Request type without [CacheResponse] — passthrough expected.
public readonly record struct UncachedRequest(int Value) : IRequest<int>;

// Request type with [CacheResponse] — caching expected.
[CacheResponse(TtlMs = 5000)]
public readonly record struct CachedRequest(int Value) : IRequest<int>;

// Request type with sliding [CacheResponse].
[CacheResponse(TtlMs = 1000, Sliding = true)]
public readonly record struct SlidingCachedRequest(int Value) : IRequest<string>;

[Collection("non-parallel")]
public class CacheBehaviorTests : IDisposable
{
    private readonly IMemoryCache _cache;

    public CacheBehaviorTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        CacheBehaviorState.SetCache(_cache);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task Handle_RequestWithoutAttribute_PassesThrough()
    {
        var callCount = 0;
        ValueTask<int> Next(UncachedRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value * 2);
        }

        var result1 = await CacheBehavior.Handle(new UncachedRequest(3), CancellationToken.None, Next);
        var result2 = await CacheBehavior.Handle(new UncachedRequest(3), CancellationToken.None, Next);

        Assert.Equal(6, result1);
        Assert.Equal(6, result2);
        Assert.Equal(2, callCount); // not cached — next called twice
    }

    [Fact]
    public async Task Handle_RequestWithAttribute_ReturnsCachedValueOnSecondCall()
    {
        var callCount = 0;
        ValueTask<int> Next(CachedRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value * 10);
        }

        var result1 = await CacheBehavior.Handle(new CachedRequest(7), CancellationToken.None, Next);
        var result2 = await CacheBehavior.Handle(new CachedRequest(7), CancellationToken.None, Next);

        Assert.Equal(70, result1);
        Assert.Equal(70, result2);
        Assert.Equal(1, callCount); // next called only once; second call hits cache
    }

    [Fact]
    public async Task Handle_DifferentRequestValues_CachedSeparately()
    {
        var callCount = 0;
        ValueTask<int> Next(CachedRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult(r.Value);
        }

        var result1 = await CacheBehavior.Handle(new CachedRequest(1), CancellationToken.None, Next);
        var result2 = await CacheBehavior.Handle(new CachedRequest(2), CancellationToken.None, Next);

        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(2, callCount); // distinct keys — both invocations hit next
    }

    [Fact]
    public async Task Handle_SlidingCachedRequest_EntriesHitCache()
    {
        var callCount = 0;
        ValueTask<string> Next(SlidingCachedRequest r, CancellationToken c)
        {
            callCount++;
            return ValueTask.FromResult($"v{r.Value}");
        }

        var result1 = await CacheBehavior.Handle(new SlidingCachedRequest(5), CancellationToken.None, Next);
        var result2 = await CacheBehavior.Handle(new SlidingCachedRequest(5), CancellationToken.None, Next);

        Assert.Equal("v5", result1);
        Assert.Equal("v5", result2);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Handle_CacheNotConfigured_ThrowsInvalidOperationException()
    {
        CacheBehaviorState.SetCache(null!);
        try
        {
            ValueTask<int> Next(CachedRequest r, CancellationToken c) => ValueTask.FromResult(0);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CacheBehavior.Handle(new CachedRequest(1), CancellationToken.None, Next).AsTask());
        }
        finally
        {
            CacheBehaviorState.SetCache(_cache);
        }
    }

    [Fact]
    public void AddMediatorCache_RegistersMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddMediatorCache();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IMemoryCache>());
    }

    [Fact]
    public void AddMediatorCache_ResolvingAccessorWiresCacheBehaviorState()
    {
        var services = new ServiceCollection();
        services.AddMediatorCache();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<MediatorCacheAccessor>();

        Assert.NotNull(CacheBehaviorState.Cache);

        // Restore for other tests in the same run.
        CacheBehaviorState.SetCache(_cache);
    }
}
