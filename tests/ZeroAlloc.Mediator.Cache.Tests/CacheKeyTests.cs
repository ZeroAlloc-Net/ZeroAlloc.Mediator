using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mediator.Cache.Tests;

// Formats itself into a span, so a cache hit needs no string for the request either.
[CacheResponse(TtlMs = 60_000)]
public readonly record struct FormattableCachedRequest(int Value) : IRequest<int>, ISpanFormattable
{
    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => string.Create(formatProvider, $"FormattableCachedRequest {{ Value = {Value} }}");

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => destination.TryWrite(provider, $"FormattableCachedRequest {{ Value = {Value} }}", out charsWritten);
}

// A key far longer than any stack buffer the lookup formats into.
[CacheResponse(TtlMs = 60_000)]
public readonly record struct LongKeyCachedRequest(string Text) : IRequest<int>;

public sealed class FormattableCachedRequestHandler : IRequestHandler<FormattableCachedRequest, int>
{
    public ValueTask<int> Handle(FormattableCachedRequest request, CancellationToken ct) => ValueTask.FromResult(0);
}

public sealed class LongKeyCachedRequestHandler : IRequestHandler<LongKeyCachedRequest, int>
{
    public ValueTask<int> Handle(LongKeyCachedRequest request, CancellationToken ct) => ValueTask.FromResult(0);
}

[Collection("non-parallel")]
public sealed class CacheKeyTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public CacheKeyTests() => CacheBehaviorState.SetCache(_cache);

    public void Dispose()
    {
        CacheBehaviorState.Cache = null;
        _cache.Dispose();
    }

    // The entry key is "<request type full name>:<request>". It is the string an app removes or
    // replaces an entry by, so the hit path may look it up differently but must find the same key.
    [Fact]
    public async Task Miss_StoresTheResponseUnderTheTypeNameAndRequestString()
    {
        var request = new CachedRequest(7);
        await CacheBehavior.Handle(request, CancellationToken.None, static (r, _) => ValueTask.FromResult(r.Value * 10));

        Assert.True(_cache.TryGetValue("ZeroAlloc.Mediator.Cache.Tests.CachedRequest:CachedRequest { Value = 7 }", out var stored));
        Assert.Equal(70, stored);
    }

    [Fact]
    public async Task Hit_FindsAnEntryStoredUnderTheStringKey()
    {
        _cache.Set("ZeroAlloc.Mediator.Cache.Tests.CachedRequest:CachedRequest { Value = 3 }", 33);

        var result = await CacheBehavior.Handle<CachedRequest, int>(new CachedRequest(3), CancellationToken.None,
            static (_, _) => throw new InvalidOperationException("The entry was there; next must not run."));

        Assert.Equal(33, result);
    }

    [Fact]
    public async Task Hit_WithAKeyLongerThanTheLookupBuffer_StillHits()
    {
        var request = new LongKeyCachedRequest(new string('x', 4096));
        var calls = 0;
        ValueTask<int> Next(LongKeyCachedRequest r, CancellationToken ct) => ValueTask.FromResult(++calls);

        var first = await CacheBehavior.Handle(request, CancellationToken.None, Next);
        var second = await CacheBehavior.Handle(request, CancellationToken.None, Next);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, calls);
    }

    // Any IMemoryCache other than MemoryCache has no span lookup and is looked up by the string.
    [Fact]
    public async Task AnotherIMemoryCache_StoresAndHitsUnderTheSameStringKey()
    {
        using var inner = new MemoryCache(new MemoryCacheOptions());
        CacheBehaviorState.SetCache(new ForwardingMemoryCache(inner));
        var calls = 0;
        ValueTask<int> Next(CachedRequest r, CancellationToken ct) => ValueTask.FromResult(++calls);

        var first = await CacheBehavior.Handle(new CachedRequest(4), CancellationToken.None, Next);
        var second = await CacheBehavior.Handle(new CachedRequest(4), CancellationToken.None, Next);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.True(inner.TryGetValue("ZeroAlloc.Mediator.Cache.Tests.CachedRequest:CachedRequest { Value = 4 }", out _));
    }

    [Fact]
    public async Task Hit_UsesTheCurrentCultureLikeTheStoredKey()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
        try
        {
            var request = new DecimalCachedRequest(1.5m);
            await CacheBehavior.Handle(request, CancellationToken.None, static (_, _) => ValueTask.FromResult(15));

            Assert.True(_cache.TryGetValue("ZeroAlloc.Mediator.Cache.Tests.DecimalCachedRequest:DecimalCachedRequest { Value = 1,5 }", out _));
            var hit = await CacheBehavior.Handle<DecimalCachedRequest, int>(request, CancellationToken.None,
                static (_, _) => throw new InvalidOperationException("Expected a hit."));
            Assert.Equal(15, hit);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

#if NET9_0_OR_GREATER
    // The hit formats the key into a stack buffer and looks it up as a span, so a request that
    // formats itself into a span costs nothing on a hit.
    [Fact]
    public async Task Hit_OfASpanFormattableRequest_AllocatesNothing()
    {
        var request = new FormattableCachedRequest(5);
        await CacheBehavior.Handle(request, CancellationToken.None, static (r, _) => ValueTask.FromResult(r.Value));

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => CacheBehavior.Handle<FormattableCachedRequest, int>(request, CancellationToken.None,
                static (_, _) => throw new InvalidOperationException("Expected a hit.")),
            "CacheBehavior.Handle (hit, ISpanFormattable request)");
    }

    // A record formats itself through ToString, which builds a string. That string is the only
    // allocation a hit may make: the key around it is formatted on the stack.
    [Fact]
    public async Task Hit_OfARecordRequest_AllocatesNoMoreThanItsToString()
    {
        var request = new CachedRequest(9);
        await CacheBehavior.Handle<CachedRequest, int>(request, CancellationToken.None, static (r, _) => ValueTask.FromResult(r.Value));

        AllocationGate.AssertNoMoreThanValueTask(1000,
            () => ValueTask.FromResult(request.ToString().Length),
            () => CacheBehavior.Handle<CachedRequest, int>(request, CancellationToken.None,
                static (_, _) => throw new InvalidOperationException("Expected a hit.")),
            "CacheBehavior.Handle (hit, record request) against request.ToString()");
    }
#endif
}

[CacheResponse(TtlMs = 60_000)]
public readonly record struct DecimalCachedRequest(decimal Value) : IRequest<int>;

public sealed class DecimalCachedRequestHandler : IRequestHandler<DecimalCachedRequest, int>
{
    public ValueTask<int> Handle(DecimalCachedRequest request, CancellationToken ct) => ValueTask.FromResult(0);
}

internal sealed class ForwardingMemoryCache(IMemoryCache inner) : IMemoryCache
{
    public ICacheEntry CreateEntry(object key) => inner.CreateEntry(key);

    public void Remove(object key) => inner.Remove(key);

    public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);

    public void Dispose()
    {
        // The test owns the inner cache.
    }
}
