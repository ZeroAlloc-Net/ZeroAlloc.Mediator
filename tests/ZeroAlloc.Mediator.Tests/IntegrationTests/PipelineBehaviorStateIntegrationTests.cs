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

public readonly record struct ServicesArgumentProbe : IRequest<int>;

public sealed class ServicesArgumentProbeHandler : IRequestHandler<ServicesArgumentProbe, int>
{
    public ValueTask<int> Handle(ServicesArgumentProbe request, CancellationToken ct) => ValueTask.FromResult(1);
}

// Takes the provider as a Handle argument. Every other behavior on this request, the bridges
// this test project references, either takes it too or opts out, so IMediator.Send passes it in
// and leaves PipelineBehaviorServices.Current alone.
[PipelineBehavior(Order = 3, AppliesTo = typeof(ServicesArgumentProbe))]
public sealed class ServicesArgumentProbeBehavior : IPipelineBehavior
{
    internal static IServiceProvider? Received;
    internal static IServiceProvider? ObservedCurrent;

    public static ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        Received = null;
        return next(request, ct);
    }

    public static async ValueTask<TResponse> Handle<TRequest, TResponse>(
        TRequest request, IServiceProvider services, CancellationToken ct,
        Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
        where TRequest : IRequest<TResponse>
    {
        await Task.Yield();
        Received = services;
        ObservedCurrent = PipelineBehaviorServices.Current;
        return await next(request, ct).ConfigureAwait(false);
    }
}

[Collection("pipeline-behavior-services")]
public sealed class PipelineBehaviorServicesIntegrationTests
{
    [Fact]
    public async Task IMediator_from_a_scope_passes_that_scope_to_a_behavior_that_takes_it()
    {
        var services = new ServiceCollection();
        services.AddTransient<ServicesArgumentProbeHandler>();
        services.AddMediator();
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        ServicesArgumentProbeBehavior.Received = null;
        ServicesArgumentProbeBehavior.ObservedCurrent = root;

        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ServicesArgumentProbe(), CancellationToken.None);

        Assert.Same(scope.ServiceProvider, ServicesArgumentProbeBehavior.Received);
        // No behavior on this request reads Current, so Send skipped the AsyncLocal write.
        Assert.Null(ServicesArgumentProbeBehavior.ObservedCurrent);
    }

    [Fact]
    public async Task Static_Send_calls_the_overload_without_a_provider()
    {
        ServicesArgumentProbeBehavior.Received = new ServiceCollection().BuildServiceProvider();

        await Mediator.Send(new ServicesArgumentProbe(), CancellationToken.None);

        Assert.Null(ServicesArgumentProbeBehavior.Received);
    }

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
