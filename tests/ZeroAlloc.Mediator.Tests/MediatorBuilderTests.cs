using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

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
    public void AddMediator_RegistersIMediatorAsSingleton_ResolvingToMediatorService()
    {
        var services = new ServiceCollection();
        services.AddMediator();

        using var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<IMediator>();

        Assert.IsType<MediatorService>(resolved);

        // Singleton: second resolve returns the same instance.
        var resolvedAgain = sp.GetRequiredService<IMediator>();
        Assert.Same(resolved, resolvedAgain);
    }

    [Fact]
    public void AddMediator_IsIdempotent_TryAddSingletonHandlesDuplicateCalls()
    {
        var services = new ServiceCollection();

        services.AddMediator();
        services.AddMediator();   // second call must not double-register

        var iMediatorRegistrations = services.Count(d => d.ServiceType == typeof(IMediator));
        Assert.Equal(1, iMediatorRegistrations);
    }
}
