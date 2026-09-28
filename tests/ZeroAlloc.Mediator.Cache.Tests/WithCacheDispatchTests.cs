using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Cache.Tests;

// Sends through the generated mediator. CacheBehavior lives in the ZeroAlloc.Mediator.Cache
// assembly, not in this one, so these pass only when the generator puts behaviors from
// referenced assemblies into the pipeline.
[Collection("non-parallel")]
public sealed class WithCacheDispatchTests : IDisposable
{
    public WithCacheDispatchTests()
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

    [Fact]
    public async Task SecondSend_ReturnsTheCachedResponse()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new CountedRequest(7));
        var second = await mediator.Send(new CountedRequest(7));

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task SecondSend_DoesNotInvokeTheHandler()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new CountedRequest(7));
        await mediator.Send(new CountedRequest(7));

        Assert.Equal(1, CountedRequestHandler.Calls);
    }

    [Fact]
    public async Task StaticSend_UsesTheContainersCache()
    {
        using var provider = BuildProvider();
        _ = provider.GetRequiredService<IMediator>();

        var first = await Mediator.Send(new CountedRequest(3));
        var second = await Mediator.Send(new CountedRequest(3));

        Assert.Equal(first, second);
        Assert.Equal(1, CountedRequestHandler.Calls);
    }

    [Fact]
    public async Task Cache_SurvivesAcrossScopes()
    {
        using var provider = BuildProvider();

        int first;
        using (var scope = provider.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CountedRequest(5));
        }

        int second;
        using (var scope = provider.CreateScope())
        {
            second = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new CountedRequest(5));
        }

        Assert.Equal(first, second);
        Assert.Equal(1, CountedRequestHandler.Calls);
    }
}
