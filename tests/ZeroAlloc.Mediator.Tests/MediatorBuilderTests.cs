using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Cache;
using ZeroAlloc.Mediator.Resilience;
using ZeroAlloc.Mediator.Validation;

namespace ZeroAlloc.Mediator.Tests;

public class MediatorBuilderTests
{
    [Fact]
    public void AddMediator_ReturnsBuilder_BackedBySameServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = services.AddMediator();

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IMediatorBuilder>(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMediator_RegistersIMediatorAsTransient_ResolvingToMediatorService()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IMediator>();

        Assert.IsType<MediatorService>(resolved);

        // Transient: each resolve returns a new instance.
        var resolvedAgain = sp.GetRequiredService<IMediator>();
        Assert.NotSame(resolved, resolvedAgain);
    }

    [Fact]
    public void AddMediator_IsIdempotent_TryAddTransientHandlesDuplicateCalls()
    {
        var services = new ServiceCollection();

        services.AddMediator();
        services.AddMediator();   // second call must not double-register

        var iMediatorRegistrations = services.Count(d => d.ServiceType == typeof(IMediator));
        Assert.Equal(1, iMediatorRegistrations);
    }

    [Fact]
    public void IMediatorBuilder_Create_BuildsBuilder_BackedBySameServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = IMediatorBuilder.Create(services);

        Assert.NotNull(builder);
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddMediator_WithCacheValidationResilience_ChainResolvesEachAccessor()
    {
        var services = new ServiceCollection();

        services.AddMediator()
                .WithCache()
                .WithValidation()
                .WithResilience();

        using var sp = services.BuildServiceProvider();

        Assert.IsType<MediatorService>(sp.GetRequiredService<IMediator>());

        // Accessor types are internal to the bridge packages; verify wiring via descriptor inspection.
        Assert.Contains(services, d => d.ServiceType.Name == "MediatorCacheAccessor");
        Assert.Contains(services, d => d.ServiceType.Name == "ValidationBehaviorAccessor");
        Assert.Contains(services, d => d.ServiceType.Name == "MediatorResilienceMarker");
    }
}
