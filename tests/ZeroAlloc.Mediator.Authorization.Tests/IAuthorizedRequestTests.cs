using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Covers IAuthorizedRequest<TResponse>: a Result-shaped deny path that returns
// Result<T, AuthorizationFailure>.Failure(...) rather than throwing
// AuthorizationDeniedException. Drives AuthorizationBehavior.Handle directly because the
// cross-assembly behavior is not auto-wired into MediatorService (the Mediator generator
// only sees [PipelineBehavior]-decorated types in the current compilation).
[Collection("non-parallel-authorization")]
public sealed class IAuthorizedRequestTests
{
    [Fact]
    public void IAuthorizedRequest_Extends_IRequest_Of_ResultWrappedResponse()
    {
        var iface = typeof(IAuthorizedRequest<int>);
        Assert.True(iface.IsInterface);

        var implementsRequest = iface.GetInterfaces()
            .Any(t => t.IsGenericType
                   && t.GetGenericTypeDefinition() == typeof(IRequest<>)
                   && t.GenericTypeArguments[0].IsGenericType
                   && t.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(Result<,>)
                   && t.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(int)
                   && t.GenericTypeArguments[0].GenericTypeArguments[1] == typeof(AuthorizationFailure));
        Assert.True(implementsRequest, "IAuthorizedRequest<T> must extend IRequest<Result<T, AuthorizationFailure>>");
    }

    [Fact]
    public async Task IAuthorizedRequest_DenyReturnsFailureResult_NotThrow()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;

        var result = await AuthorizationBehavior.Handle<GetThingResultDeny, Result<int, AuthorizationFailure>>(
            new GetThingResultDeny(5), CancellationToken.None,
            static (r, _) => new(Result<int, AuthorizationFailure>.Success(r.Id)));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task IAuthorizedRequest_AllowReturnsSuccessResultWithPayload()
    {
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;

        var result = await AuthorizationBehavior.Handle<GetThingResultAllow, Result<int, AuthorizationFailure>>(
            new GetThingResultAllow(7), CancellationToken.None,
            static (r, _) => new(Result<int, AuthorizationFailure>.Success(r.Id * 3)));

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Value);
    }

    [Fact]
    public async Task IAuthorizedRequest_FailureRoundTripsCodeAndReason()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;

        var result = await AuthorizationBehavior.Handle<GetThingResultDeny, Result<int, AuthorizationFailure>>(
            new GetThingResultDeny(1), CancellationToken.None,
            static (r, _) => new(Result<int, AuthorizationFailure>.Success(r.Id)));

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, result.Error.Code);
        Assert.Equal("Denied", result.Error.Reason);
    }

    private static ServiceProvider BuildProvider(ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }
}
