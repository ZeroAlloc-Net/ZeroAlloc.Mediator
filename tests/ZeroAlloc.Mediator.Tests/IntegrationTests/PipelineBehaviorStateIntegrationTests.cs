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

// Records the state a behavior receives, after an await so the value must have flowed with
// the call rather than been read on the thread that started Send.
[PipelineBehavior(Order = 2, AppliesTo = typeof(ServicesProbe))]
public sealed class ServicesProbeBehavior : IPipelineBehavior
{
    internal static IServiceProvider? Observed;

    public static async ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
        TRequest request, TState state, CancellationToken ct,
        Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
        where TState : IServiceProvider?
    {
        await Task.Yield();
        Observed = state;
        return await next(request, state, ct).ConfigureAwait(false);
    }
}

[Collection("pipeline-behavior-state")]
public sealed class PipelineBehaviorStateIntegrationTests
{
    [Fact]
    public async Task IMediator_from_a_scope_hands_that_scope_to_the_pipeline_as_its_state()
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
    public async Task Static_Send_hands_the_pipeline_a_null_state()
    {
        ServicesProbeBehavior.Observed = new ServiceCollection().BuildServiceProvider();

        await Mediator.Send(new ServicesProbe(), CancellationToken.None);

        Assert.Null(ServicesProbeBehavior.Observed);
    }
}

[CollectionDefinition("pipeline-behavior-state", DisableParallelization = true)]
public sealed class PipelineBehaviorStateCollection { }
