using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using Xunit;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Integration tests covering backlog items #4 (pipeline ordering) and #5
// (end-to-end via IMediator.Send). These complement AuthorizationBehaviorTests,
// which drives AuthorizationBehavior.Handle directly with a mocked next
// delegate. The tests here exercise the full chain: DI container build →
// AuthorizationBehaviorAccessor sets the static → Mediator generator's
// dispatcher routes IMediator.Send through AuthorizationBehaviorShim →
// shim forwards to AuthorizationBehavior.Handle → AuthorizerFor + policy +
// CountingDownstreamBehavior + handler.
//
// All three tests use [Collection("non-parallel-authorization")] because
// AuthorizationBehaviorState.ServiceProvider is a static field; running in
// parallel would stomp it across tests.
//
// Swap-test for the ordering assertion's meaningfulness: temporarily change
// CountingDownstreamBehavior's attribute in TestFixtures.cs to
// [PipelineBehavior(Order = -2000)] (numerically BEFORE the shim's -1000).
// The Pipeline_ordering_authorization_runs_before_later_behaviors test must
// then FAIL with counter.Count == 1 — proving the test catches a real
// regression in pipeline ordering. Revert after verifying. Recommended any
// time the Mediator core's pipeline-ordering algorithm changes.
[Collection("non-parallel-authorization")]
public sealed class IntegrationTests
{
    [Fact]
    public async Task Pipeline_ordering_authorization_runs_before_later_behaviors()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With()); // no Admin → policy denies
        var mediator = sp.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await mediator.Send(new IntegrationTestRequest(42)));

        // Proves ordering: CountingDownstreamBehavior (Order=-500) never ran
        // because AuthorizationBehaviorShim (Order=-1000) short-circuited
        // ahead of it via AuthorizationDeniedException.
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public async Task End_to_end_through_IMediator_Send_allow_path()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With("Admin"));
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new IntegrationTestRequest(7));

        Assert.Equal(14, result);          // handler invoked: 7 * 2
        Assert.Equal(1, counter.Count);    // downstream behavior ran on the allow path
    }

    [Fact]
    public async Task End_to_end_through_IMediator_Send_deny_path()
    {
        var counter = new InvocationCounter();
        using var sp = BuildProvider(counter, TestSecurityContexts.With()); // no Admin
        var mediator = sp.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
            await mediator.Send(new IntegrationTestRequest(7)));

        // Handler must NOT have run — the shim short-circuited via the throw.
        Assert.Equal(0, counter.Count);
    }

    private static ServiceProvider BuildProvider(InvocationCounter counter, ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddSingleton(counter);
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        services.AddTransient<IntegrationTestHandler>();
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        var sp = services.BuildServiceProvider();

        // Trigger AuthorizationBehaviorAccessor's ctor → sets the static
        // ServiceProvider so the shim can resolve AuthorizerFor + the
        // security context per request. Equivalent to what
        // AuthorizationBehaviorState.ServiceProvider = sp would do via
        // internals, but goes through the public accessor.
        _ = sp.GetRequiredService<AuthorizationBehaviorAccessor>();
        return sp;
    }
}
