using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Cache;
using ZeroAlloc.Mediator.Resilience;
using ZeroAlloc.Mediator.Validation;

namespace ZeroAlloc.Mediator.Tests;

// Resolving IMediator from a container wired with WithCache or WithValidation points the bridge
// packages' process-wide behavior state at that container, and disposing it leaves any dispatch
// already holding the reference with a disposed provider. A collection without parallelization
// runs alone after every parallel test, so no other test dispatches while this one owns the state.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PipelineBehaviorStateCollection
{
    public const string Name = "pipeline-behavior-state";
}

[Collection(PipelineBehaviorStateCollection.Name)]
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
    public void AddMediator_RegistersPipelineBehaviorStateActivation_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddMediator();
        services.AddMediator();

        var registration = Assert.Single(services, d => d.ServiceType == typeof(PipelineBehaviorStateActivation));
        Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [Fact]
    public void MediatorBuilder_Construction_IsBackedBySameServiceCollection()
    {
        var services = new ServiceCollection();

        var builder = new MediatorBuilder(services);

        Assert.NotNull(builder);
        Assert.Same(services, builder.Services);
        Assert.IsAssignableFrom<IMediatorBuilder>(builder);
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
