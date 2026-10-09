using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct RegLifetimeProbe(int X) : IRequest<RegLifetimeProbeHandler>;

public class RegLifetimeProbeHandler : IRequestHandler<RegLifetimeProbe, RegLifetimeProbeHandler>
{
    public ValueTask<RegLifetimeProbeHandler> Handle(RegLifetimeProbe request, CancellationToken ct)
        => ValueTask.FromResult(this);
}

public readonly record struct RegSingletonProbe(int X) : IRequest<RegSingletonProbeHandler>;

[HandlerLifetime(ServiceLifetime.Singleton)]
public class RegSingletonProbeHandler : IRequestHandler<RegSingletonProbe, RegSingletonProbeHandler>
{
    public ValueTask<RegSingletonProbeHandler> Handle(RegSingletonProbe request, CancellationToken ct)
        => ValueTask.FromResult(this);
}

public readonly record struct RegInternalPing(int X) : IRequest<int>;

internal class RegInternalPingHandler : IRequestHandler<RegInternalPing, int>
{
    public ValueTask<int> Handle(RegInternalPing request, CancellationToken ct) => ValueTask.FromResult(request.X + 1);
}

public class GeneratedRegistrationIntegrationTests
{
    [Fact]
    public async Task AddMediator_Alone_ResolvesHandlers()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();

        var result = await sp.GetRequiredService<IMediator>().Send(new IntegrationPing("x"), CancellationToken.None);

        Assert.Equal("Pong: x", result);
    }

    [Fact]
    public async Task AddMediator_Alone_ResolvesInternalHandlers()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();

        Assert.Equal(2, await sp.GetRequiredService<IMediator>().Send(new RegInternalPing(1), CancellationToken.None));
    }

    [Fact]
    public async Task AddMediator_DefaultLifetime_IsTransient()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var first = await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None);
        var second = await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task AddMediator_WithScopedDefault_SharesWithinAScope_NotAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddMediator(ServiceLifetime.Scoped);
        using var sp = services.BuildServiceProvider();

        RegLifetimeProbeHandler a1, a2, b1;
        using (var scope = sp.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            a1 = await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None);
            a2 = await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None);
        }
        using (var scope = sp.CreateScope())
        {
            b1 = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegLifetimeProbe(3), CancellationToken.None);
        }

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b1);
    }

    [Fact]
    public async Task HandlerLifetimeAttribute_WinsOverTheDefault()
    {
        var services = new ServiceCollection();
        // A Scoped default, so a shared instance across scopes can only come from the attribute.
        services.AddMediator(ServiceLifetime.Scoped);
        using var sp = services.BuildServiceProvider();

        RegSingletonProbeHandler a, b;
        using (var scope = sp.CreateScope())
        {
            a = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegSingletonProbe(1), CancellationToken.None);
        }
        using (var scope = sp.CreateScope())
        {
            b = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RegSingletonProbe(2), CancellationToken.None);
        }

        Assert.Same(a, b);
    }

    [Fact]
    public async Task CallerRegistration_BeforeAddMediator_IsKept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RegLifetimeProbeHandler>();
        services.AddMediator();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        Assert.Same(
            await mediator.Send(new RegLifetimeProbe(1), CancellationToken.None),
            await mediator.Send(new RegLifetimeProbe(2), CancellationToken.None));
        Assert.Single(services, d => d.ServiceType == typeof(RegLifetimeProbeHandler));
    }
}
