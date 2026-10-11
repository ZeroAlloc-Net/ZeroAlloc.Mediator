using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public readonly record struct ServicesProbe : IRequest<int>;

public sealed class ServicesProbeHandler : IRequestHandler<ServicesProbe, int>
{
    public ValueTask<int> Handle(ServicesProbe request, CancellationToken ct) => ValueTask.FromResult(1);
}

// Records the provider a behavior sees, after an await so the value must have flowed with the
// async call rather than been read on the thread that started Send.
[PipelineBehavior(Order = 2, AppliesTo = typeof(ServicesProbe))]
public sealed class ServicesProbeBehavior : IPipelineBehavior
{
    internal static IServiceProvider? Observed;

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        await Task.Yield();
        Observed = PipelineBehaviorServices.Current;
        return await next(request, ct).ConfigureAwait(false);
    }
}

[Collection("pipeline-behavior-services")]
public sealed class PipelineBehaviorServicesIntegrationTests
{
    [Fact]
    public async Task IMediator_from_a_scope_hands_that_scope_to_the_pipeline()
    {
        var services = new ServiceCollection();
        services.AddTransient<ServicesProbeHandler>();
        services.AddMediator();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        ServicesProbeBehavior.Observed = null;

        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ServicesProbe(), CancellationToken.None);

        Assert.Same(scope.ServiceProvider, ServicesProbeBehavior.Observed);
    }

    [Fact]
    public async Task The_provider_does_not_outlive_Send()
    {
        var services = new ServiceCollection();
        services.AddTransient<ServicesProbeHandler>();
        services.AddMediator();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ServicesProbe(), CancellationToken.None);

        Assert.Null(PipelineBehaviorServices.Current);
    }

    [Fact]
    public async Task Static_Send_hands_the_pipeline_no_provider()
    {
        ServicesProbeBehavior.Observed = new ServiceCollection().BuildServiceProvider();

        await Mediator.Send(new ServicesProbe(), CancellationToken.None);

        Assert.Null(ServicesProbeBehavior.Observed);
    }
}

[CollectionDefinition("pipeline-behavior-services", DisableParallelization = true)]
public sealed class PipelineBehaviorServicesCollection { }
