// Notification dispatch strategy benchmarks — ZeroAlloc.Saga#127.
//
// Mediator's generated Publish resolves each handler by its CONCRETE type
// (GetRequiredService<TConcreteHandler>). ZeroAlloc.Saga registers its generated handlers by
// INTERFACE (AddTransient<INotificationHandler<T>, TConcrete>). The two contracts do not meet,
// so a saga never receives its trigger event.
//
// The proposed fix has the generated Publish ALSO enumerate INotificationHandler<T> from DI.
// This project measures what that costs, so the "always enumerate vs. gate it" decision is
// made on numbers rather than intuition.
//
// Every dispatch shape here is hand-written rather than generator-emitted, on purpose:
//   * it isolates the resolution strategy from unrelated generator output, and
//   * the candidate strategies do not exist in the generator yet.
// The ActivitySource span the generator wraps around Publish is omitted — it is identical
// across all strategies and would only add a constant to every row.
//
// Handler bodies return ValueTask.CompletedTask, so what is measured is resolution +
// invocation overhead, not handler work.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

BenchmarkRunner.Run<NotificationDispatchBenchmarks>(
    DefaultConfig.Instance
        .HideColumns(Column.Error, Column.StdDev, Column.Median, Column.RatioSD));

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class NotificationDispatchBenchmarks
{
    private readonly CancellationToken _ct = CancellationToken.None;
    private ServiceProvider _root = null!;
    private IServiceScope _scope = null!;
    private IServiceProvider _sp = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Mediator's contract: handlers registered by concrete type. This is exactly what
        // MediatorBuilderExtensions.RegisterHandlersFromAssembly does today
        // (new ServiceDescriptor(type, type, lifetime)), and what generated Publish resolves.
        services.AddTransient<OneHandler>();
        services.AddTransient<ThreeHandlerA>();
        services.AddTransient<ThreeHandlerB>();
        services.AddTransient<ThreeHandlerC>();
        services.AddTransient<UnreachedHandler>();

        // Saga's contract: handlers registered by interface. This is what
        // BuilderExtensionsEmitter emits in With{Saga}Saga().
        services.AddTransient<INotificationHandler<OneEvent>, OneHandler>();
        services.AddTransient<INotificationHandler<ThreeEvent>, ThreeHandlerA>();
        services.AddTransient<INotificationHandler<ThreeEvent>, ThreeHandlerB>();
        services.AddTransient<INotificationHandler<ThreeEvent>, ThreeHandlerC>();

        // UnreachedEvent is deliberately NOT registered by interface. It models the existing
        // user who never touches Saga — the "NoInterfaceRegs" category measures the tax an
        // unconditional enumeration would levy on them.

        _root = services.BuildServiceProvider();
        _scope = _root.CreateScope();
        _sp = _scope.ServiceProvider;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _root.Dispose();
    }

    // ============================================================
    // One handler
    // ============================================================

    /// <summary>Today's emitted shape.</summary>
    [BenchmarkCategory("N=1"), Benchmark(Baseline = true)]
    public async ValueTask Concrete_1()
    {
        var n = new OneEvent(42);
        await _sp.GetRequiredService<OneHandler>().Handle(n, _ct).ConfigureAwait(false);
    }

    /// <summary>DI enumeration replacing the concrete resolution outright.</summary>
    [BenchmarkCategory("N=1"), Benchmark]
    public async ValueTask Enumerable_1()
    {
        var n = new OneEvent(42);
        foreach (var h in _sp.GetServices<INotificationHandler<OneEvent>>())
            await h.Handle(n, _ct).ConfigureAwait(false);
    }

    /// <summary>The proposal's lower bound: keep the concrete calls, add enumeration, no dedup.
    /// A handler registered under both contracts runs TWICE here — not shippable, measured only
    /// to separate enumeration cost from dedup cost.</summary>
    [BenchmarkCategory("N=1"), Benchmark]
    public async ValueTask ConcreteAndEnumerable_1()
    {
        var n = new OneEvent(42);
        await _sp.GetRequiredService<OneHandler>().Handle(n, _ct).ConfigureAwait(false);
        foreach (var h in _sp.GetServices<INotificationHandler<OneEvent>>())
            await h.Handle(n, _ct).ConfigureAwait(false);
    }

    /// <summary>The shippable proposal. Dedup is an inline type test rather than a HashSet
    /// because the generator knows every concrete handler type at compile time and can emit
    /// exactly this — allocation-free.</summary>
    [BenchmarkCategory("N=1"), Benchmark]
    public async ValueTask ConcreteAndEnumerableDeduped_1()
    {
        var n = new OneEvent(42);
        await _sp.GetRequiredService<OneHandler>().Handle(n, _ct).ConfigureAwait(false);
        foreach (var h in _sp.GetServices<INotificationHandler<OneEvent>>())
        {
            if (h is OneHandler) continue;
            await h.Handle(n, _ct).ConfigureAwait(false);
        }
    }

    // ============================================================
    // Three handlers
    // ============================================================

    [BenchmarkCategory("N=3"), Benchmark(Baseline = true)]
    public async ValueTask Concrete_3()
    {
        var n = new ThreeEvent(42);
        await _sp.GetRequiredService<ThreeHandlerA>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerB>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerC>().Handle(n, _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("N=3"), Benchmark]
    public async ValueTask Enumerable_3()
    {
        var n = new ThreeEvent(42);
        foreach (var h in _sp.GetServices<INotificationHandler<ThreeEvent>>())
            await h.Handle(n, _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("N=3"), Benchmark]
    public async ValueTask ConcreteAndEnumerable_3()
    {
        var n = new ThreeEvent(42);
        await _sp.GetRequiredService<ThreeHandlerA>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerB>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerC>().Handle(n, _ct).ConfigureAwait(false);
        foreach (var h in _sp.GetServices<INotificationHandler<ThreeEvent>>())
            await h.Handle(n, _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("N=3"), Benchmark]
    public async ValueTask ConcreteAndEnumerableDeduped_3()
    {
        var n = new ThreeEvent(42);
        await _sp.GetRequiredService<ThreeHandlerA>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerB>().Handle(n, _ct).ConfigureAwait(false);
        await _sp.GetRequiredService<ThreeHandlerC>().Handle(n, _ct).ConfigureAwait(false);
        foreach (var h in _sp.GetServices<INotificationHandler<ThreeEvent>>())
        {
            if (h is ThreeHandlerA or ThreeHandlerB or ThreeHandlerC) continue;
            await h.Handle(n, _ct).ConfigureAwait(false);
        }
    }

    // ============================================================
    // The tax on users who never register by interface
    //
    // This is the category that decides "always enumerate" vs. "gate it". Every existing
    // Mediator user falls here: the enumeration finds nothing and its entire cost is waste.
    // ============================================================

    [BenchmarkCategory("NoInterfaceRegs"), Benchmark(Baseline = true)]
    public async ValueTask Concrete_NoInterfaceRegs()
    {
        var n = new UnreachedEvent(42);
        await _sp.GetRequiredService<UnreachedHandler>().Handle(n, _ct).ConfigureAwait(false);
    }

    [BenchmarkCategory("NoInterfaceRegs"), Benchmark]
    public async ValueTask ConcreteAndEmptyEnumerable_NoInterfaceRegs()
    {
        var n = new UnreachedEvent(42);
        await _sp.GetRequiredService<UnreachedHandler>().Handle(n, _ct).ConfigureAwait(false);
        foreach (var h in _sp.GetServices<INotificationHandler<UnreachedEvent>>())
            await h.Handle(n, _ct).ConfigureAwait(false);
    }
}

// ============================================================
// Notifications
// ============================================================

public readonly record struct OneEvent(int Id) : INotification;
public readonly record struct ThreeEvent(int Id) : INotification;
public readonly record struct UnreachedEvent(int Id) : INotification;

// ============================================================
// Handlers
// ============================================================

public sealed class OneHandler : INotificationHandler<OneEvent>
{
    public ValueTask Handle(OneEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed class ThreeHandlerA : INotificationHandler<ThreeEvent>
{
    public ValueTask Handle(ThreeEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed class ThreeHandlerB : INotificationHandler<ThreeEvent>
{
    public ValueTask Handle(ThreeEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed class ThreeHandlerC : INotificationHandler<ThreeEvent>
{
    public ValueTask Handle(ThreeEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public sealed class UnreachedHandler : INotificationHandler<UnreachedEvent>
{
    public ValueTask Handle(UnreachedEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}
