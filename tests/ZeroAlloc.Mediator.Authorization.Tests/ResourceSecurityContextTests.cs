using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using Xunit;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Covers the v2.1-contract lighting-up wired by AuthorizationBehavior wrapping
// the resolved ISecurityContext in ResourceSecurityContextAdapter<TRequest>:
//
//   - Policies that type-check IResourceSecurityContext<TRequest> resolve and
//     see the dispatched request as Resource.
//   - Policies that type-check IResourceSecurityContext<UnrelatedRequest>
//     correctly fall through (the cast fails when TResource differs).
//
// Drives AuthorizationBehavior.Handle directly (rather than IMediator.Send)
// for the same reason AllocationBudgetTests does: the cross-assembly behavior
// is not auto-wired into the test-assembly's MediatorService partial, and the
// allocation profile is identical either way.

[Collection("non-parallel-authorization")]
public sealed class ResourceSecurityContextTests
{
    [Fact]
    public async Task Policy_Sees_DispatchedRequest_AsResource()
    {
        // TestSecurityContexts.With(...) treats positional args as roles and
        // always pins Id = "user-1". The policy compares request.UserId to
        // ctx.Id, so the request's UserId must match the helper's Id.
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        var req = new ResourceOwnerCommand(UserId: "user-1", Payload: 7);

        var response = await AuthorizationBehavior.Handle<ResourceOwnerCommand, int>(
            req, CancellationToken.None,
            static (r, _) => ValueTask.FromResult(r.Payload * 2));

        // Policy resolved IResourceSecurityContext<ResourceOwnerCommand>, saw the
        // request's UserId == ctx.Id, and allowed; handler ran (7 * 2).
        Assert.Equal(14, response);
    }

    [Fact]
    public async Task Policy_TypeChecking_WrongRequestType_FallsThrough()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        var req = new ResourceWrongTypeCommand(UserId: "user-1");

        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await AuthorizationBehavior.Handle<ResourceWrongTypeCommand, int>(
                req, CancellationToken.None,
                static (_, _) => ValueTask.FromResult(42)));

        // The policy on ResourceWrongTypeCommand type-checks
        // IResourceSecurityContext<ResourceOwnerCommand> (intentionally mismatched).
        // The runtime adapter is IResourceSecurityContext<ResourceWrongTypeCommand>,
        // so the cast fails and the policy's else branch runs.
        Assert.Equal("resource.wrong_type", ex.Failure.Code);
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
