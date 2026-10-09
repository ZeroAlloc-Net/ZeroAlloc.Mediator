using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Cache;

namespace ZeroAlloc.Mediator.AotSmoke.Cache;

#pragma warning disable MA0048
public enum CachedStatus { Unknown, Active, Closed }

[StructLayout(LayoutKind.Auto)]
public readonly record struct CachedPoint(int X, int Y);

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedInt(int Id) : IRequest<int>;

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedNullableInt(int Id) : IRequest<int?>;

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedNullableGuid(int Id) : IRequest<Guid?>;

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedNullableEnum(int Id) : IRequest<CachedStatus?>;

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedStruct(int Id) : IRequest<CachedPoint>;

[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedNullableStruct(int Id) : IRequest<CachedPoint?>;

// Sent through IMediator, so the generated Send has to run CacheBehavior. Its handler returns a
// new value on every call, so an unchanged second response can only come from the cache.
[CacheResponse(TtlMs = 60_000)]
public readonly record struct CachedThroughMediator(int Id) : IRequest<int>;

public sealed class CachedThroughMediatorHandler : IRequestHandler<CachedThroughMediator, int>
{
    internal static int Calls;

    public ValueTask<int> Handle(CachedThroughMediator r, CancellationToken ct)
        => ValueTask.FromResult((r.Id * 1000) + Interlocked.Increment(ref Calls));
}

// Stub handlers, required by ZAM001. The value-type cases drive CacheBehavior.Handle directly.
public sealed class CachedIntHandler : IRequestHandler<CachedInt, int>
{
    public ValueTask<int> Handle(CachedInt r, CancellationToken ct) => ValueTask.FromResult(r.Id);
}
public sealed class CachedNullableIntHandler : IRequestHandler<CachedNullableInt, int?>
{
    public ValueTask<int?> Handle(CachedNullableInt r, CancellationToken ct) => ValueTask.FromResult<int?>(r.Id);
}
public sealed class CachedNullableGuidHandler : IRequestHandler<CachedNullableGuid, Guid?>
{
    public ValueTask<Guid?> Handle(CachedNullableGuid r, CancellationToken ct) => ValueTask.FromResult<Guid?>(Guid.Empty);
}
public sealed class CachedNullableEnumHandler : IRequestHandler<CachedNullableEnum, CachedStatus?>
{
    public ValueTask<CachedStatus?> Handle(CachedNullableEnum r, CancellationToken ct) => ValueTask.FromResult<CachedStatus?>(CachedStatus.Active);
}
public sealed class CachedStructHandler : IRequestHandler<CachedStruct, CachedPoint>
{
    public ValueTask<CachedPoint> Handle(CachedStruct r, CancellationToken ct) => ValueTask.FromResult(new CachedPoint(r.Id, 0));
}
public sealed class CachedNullableStructHandler : IRequestHandler<CachedNullableStruct, CachedPoint?>
{
    public ValueTask<CachedPoint?> Handle(CachedNullableStruct r, CancellationToken ct) => ValueTask.FromResult<CachedPoint?>(new CachedPoint(r.Id, 0));
}
#pragma warning restore MA0048

// Drives CacheBehavior with value-type responses. The first call is a miss that stores the
// response; the second must be a hit that returns the stored value without calling next.
// The hit goes through IMemoryCache.TryGetValue<TResponse>, which does a generic type test
// that hangs under NativeAOT when TResponse is a Nullable<T>: dotnet/runtime#134799.
public static class CachedScenario
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddMediator().WithCache();
        using var provider = services.BuildServiceProvider();

        // The first IMediator resolved from the container points CacheBehavior at the
        // container's own IMemoryCache. Before #234, WithCache left it holding a disposed one.
        var mediator = provider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new CachedThroughMediator(7), CancellationToken.None).ConfigureAwait(false);
        var second = await mediator.Send(new CachedThroughMediator(7), CancellationToken.None).ConfigureAwait(false);
        if (first != second || Volatile.Read(ref CachedThroughMediatorHandler.Calls) != 1)
            throw new InvalidOperationException(
                $"IMediator.Send: expected one handler call and a cached second response, got {first} then {second}");
        Console.WriteLine("Mediator.Cache: IMediator.Send hit OK");

        await HitAsync(new CachedInt(7), 7, 42).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: int OK");
        await HitAsync(new CachedStruct(7), new CachedPoint(7, 1), new CachedPoint(0, 0)).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: struct OK");
        await HitAsync<CachedNullableInt, int?>(new CachedNullableInt(7), 7, 42).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: int? OK");
        await HitAsync<CachedNullableInt, int?>(new CachedNullableInt(8), null, 42).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: int? null OK");
        var g = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        await HitAsync<CachedNullableGuid, Guid?>(new CachedNullableGuid(7), g, Guid.Empty).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: Guid? OK");
        await HitAsync<CachedNullableEnum, CachedStatus?>(new CachedNullableEnum(7), CachedStatus.Closed, CachedStatus.Unknown).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: enum? OK");
        await HitAsync<CachedNullableStruct, CachedPoint?>(new CachedNullableStruct(7), new CachedPoint(7, 2), new CachedPoint(0, 0)).ConfigureAwait(false);
        Console.WriteLine("Mediator.Cache: struct? OK");
    }

    private static async Task HitAsync<TRequest, TResponse>(TRequest request, TResponse stored, TResponse other)
        where TRequest : IRequest<TResponse>
    {
        var calls = 0;
        ValueTask<TResponse> Next(TRequest r, CancellationToken ct)
        {
            calls++;
            return ValueTask.FromResult(calls == 1 ? stored : other);
        }

        var miss = await CacheBehavior.Handle<TRequest, TResponse>(request, CancellationToken.None, Next).ConfigureAwait(false);
        var hit = await CacheBehavior.Handle<TRequest, TResponse>(request, CancellationToken.None, Next).ConfigureAwait(false);

        if (!Equals(miss, stored))
            throw new InvalidOperationException($"{typeof(TRequest).Name}: miss returned '{miss}', expected '{stored}'");
        if (!Equals(hit, stored))
            throw new InvalidOperationException($"{typeof(TRequest).Name}: hit returned '{hit}', expected '{stored}'");
        if (calls != 1)
            throw new InvalidOperationException($"{typeof(TRequest).Name}: next called {calls} times, expected 1");
    }
}
