using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
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

    // #277: the reflection-based RegisterHandlersFromAssembly scanners are gone. The generated
    // AddMediator() registers every handler at compile time, so the runtime package exposes no
    // API that is unsafe under trimming or NativeAOT.
    [Fact]
    public void RuntimePackage_ExposesNoTrimOrAotUnsafeApi()
    {
        var assembly = typeof(IMediatorBuilder).Assembly;

        Assert.Null(assembly.GetType("ZeroAlloc.Mediator.MediatorBuilderExtensions"));

        var unsafeMembers = assembly.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(RequiresUnreferencedCodeAttribute), inherit: false)
                     || m.IsDefined(typeof(RequiresDynamicCodeAttribute), inherit: false))
            .Select(m => m.DeclaringType!.FullName + "." + m.Name)
            .ToList();
        Assert.Empty(unsafeMembers);
    }
}
