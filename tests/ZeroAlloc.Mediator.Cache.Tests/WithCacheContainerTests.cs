using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Cache.Tests;

// A cached request whose handler returns a different value on every call, so a response that
// comes back unchanged can only have come from the cache.
[CacheResponse(TtlMs = 60_000)]
public readonly record struct CountedRequest(int Id) : IRequest<int>;

public sealed class CountedRequestHandler : IRequestHandler<CountedRequest, int>
{
    private static int _calls;

    public static int Calls => Volatile.Read(ref _calls);

    public static void Reset() => Volatile.Write(ref _calls, 0);

    public ValueTask<int> Handle(CountedRequest request, CancellationToken ct)
        => ValueTask.FromResult((request.Id * 1000) + Interlocked.Increment(ref _calls));
}

// Drives AddMediator().WithCache() the way an app does: build the container, resolve IMediator,
// send. Nothing here reaches into internals to wire the cache, so a gap in the wiring fails.
[Collection("non-parallel")]
public sealed class WithCacheContainerTests : IDisposable
{
    public WithCacheContainerTests()
    {
        CacheBehaviorState.Cache = null;
        CountedRequestHandler.Reset();
    }

    public void Dispose() => CacheBehaviorState.Cache = null;

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<CountedRequestHandler>();
        services.AddMediator().WithCache();
        return services.BuildServiceProvider();
    }

    private static ValueTask<int> Next(CountedRequest request, CancellationToken ct)
        => ValueTask.FromResult(-1);

    [Fact]
    public void WithCache_DoesNotPopulateStateAtRegistration()
    {
        // Registration must not build a provider of its own. Before #234 it did, and left the
        // static pointing at that provider's MemoryCache after disposing it.
        var services = new ServiceCollection();
        services.AddMediator().WithCache();

        Assert.Null(CacheBehaviorState.Cache);
    }

    [Fact]
    public void ResolvingMediator_PointsStateAtTheContainersMemoryCache()
    {
        using var provider = BuildProvider();

        _ = provider.GetRequiredService<IMediator>();

        Assert.Same(provider.GetRequiredService<IMemoryCache>(), CacheBehaviorState.Cache);
    }

    [Fact]
    public async Task CacheBehavior_AfterResolvingMediator_ReadsALiveCache()
    {
        // The #234 repro: the first cached request threw ObjectDisposedException.
        using var provider = BuildProvider();
        _ = provider.GetRequiredService<IMediator>();

        var miss = await CacheBehavior.Handle<CountedRequest, int>(new CountedRequest(7), CancellationToken.None, (_, _) => ValueTask.FromResult(42));
        var hit = await CacheBehavior.Handle<CountedRequest, int>(new CountedRequest(7), CancellationToken.None, Next);

        Assert.Equal(42, miss);
        Assert.Equal(42, hit);
    }

    [Fact]
    public async Task DisposingTheProvider_ClearsState_SoACachedRequestFailsClearly()
    {
        var provider = BuildProvider();
        _ = provider.GetRequiredService<IMediator>();

        provider.Dispose();

        Assert.Null(CacheBehaviorState.Cache);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CacheBehavior.Handle<CountedRequest, int>(new CountedRequest(1), CancellationToken.None, Next).AsTask());
        Assert.Contains("WithCache", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposingAnotherProvider_LeavesTheLiveContainersCacheInPlace()
    {
        var first = BuildProvider();
        _ = first.GetRequiredService<IMediator>();
        using var second = BuildProvider();
        _ = second.GetRequiredService<IMediator>();

        first.Dispose();

        Assert.Same(second.GetRequiredService<IMemoryCache>(), CacheBehaviorState.Cache);
    }
}
