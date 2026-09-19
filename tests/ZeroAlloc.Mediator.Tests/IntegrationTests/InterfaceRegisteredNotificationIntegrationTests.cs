using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// Models ZeroAlloc.Saga#127 end to end: the handler is registered as
// INotificationHandler<T> (what With{Saga}Saga() emits) and NOT by concrete type
// (what RegisterHandlersFromAssembly emits). Before the fix, IMediator.Publish never
// reached it.

public readonly record struct SagaLikeTriggered(int Id) : INotification;

public sealed class SagaLikeHandler : INotificationHandler<SagaLikeTriggered>
{
    public ValueTask Handle(SagaLikeTriggered notification, CancellationToken ct)
    {
        SagaLikeCounters.InterfaceRegisteredCount++;
        return ValueTask.CompletedTask;
    }
}

public readonly record struct SagaLikeShared(int Id) : INotification;

public sealed class SagaLikeSharedHostHandler : INotificationHandler<SagaLikeShared>
{
    public ValueTask Handle(SagaLikeShared notification, CancellationToken ct)
    {
        SagaLikeCounters.HostCount++;
        return ValueTask.CompletedTask;
    }
}

// No handler for this one anywhere in source — models a saga trigger event whose only handler
// is emitted by another generator. Publish must exist for it and must not throw.
public readonly record struct SagaLikeUnhandled(int Id) : INotification;

internal static class SagaLikeCounters
{
    public static int InterfaceRegisteredCount;
    public static int HostCount;

    public static void Reset()
    {
        InterfaceRegisteredCount = 0;
        HostCount = 0;
    }
}

public class InterfaceRegisteredNotificationIntegrationTests
{
    [Fact]
    public async Task Publish_ReachesHandlerRegisteredByInterfaceOnly()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        services.AddTransient<INotificationHandler<SagaLikeTriggered>, SagaLikeHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        SagaLikeCounters.Reset();

        await mediator.Publish(new SagaLikeTriggered(1), CancellationToken.None);

        Assert.Equal(1, SagaLikeCounters.InterfaceRegisteredCount);
    }

    [Fact]
    public async Task Publish_ReachesInterfaceHandler_EvenWhenHostDeclaresItsOwn()
    {
        // The cross-assembly case from the issue: a host handler exists at compile time, so the
        // old generator emitted a Publish that dispatched ONLY to it and silently skipped the
        // interface-registered one.
        var services = new ServiceCollection();
        services.AddMediator();
        services.AddTransient<SagaLikeSharedHostHandler>();
        services.AddTransient<INotificationHandler<SagaLikeShared>, SagaLikeSharedHostHandler>();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        SagaLikeCounters.Reset();

        await mediator.Publish(new SagaLikeShared(1), CancellationToken.None);

        // Registered under BOTH contracts — must run exactly once, not twice.
        Assert.Equal(1, SagaLikeCounters.HostCount);
    }

    [Fact]
    public async Task Publish_IsNoOp_WhenNotificationHasNoHandlersAtAll()
    {
        // Publishing an event this compilation knows no handler for is legitimate — the handler
        // may be emitted by another generator and simply not registered in this container.
        var services = new ServiceCollection();
        services.AddMediator();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new SagaLikeUnhandled(1), CancellationToken.None);
    }

    [Fact]
    public async Task Publish_StillThrows_WhenAKnownHandlerIsRegisteredNowhere()
    {
        // The pre-fix diagnostic is preserved: a handler visible at compile time but registered
        // under neither contract is a misconfiguration, not a silent no-op.
        var services = new ServiceCollection();
        services.AddMediator();

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await mediator.Publish(new SagaLikeTriggered(1), CancellationToken.None));

        Assert.Contains("SagaLikeHandler", ex.Message, StringComparison.Ordinal);
    }
}
