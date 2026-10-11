using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using Xunit;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Regression tests for authorizing each request with the identity of the request being
// dispatched. The security context and the generated dispatchers are scoped. Resolved from the
// root provider, the first caller's identity was cached for the container's lifetime and every
// later request was authorized with it.
[Collection("non-parallel-authorization")]
public sealed class RequestScopeTests
{
    [Fact]
    public async Task Each_scope_is_authorized_with_its_own_identity()
    {
        using var root = BuildPerScopeIdentityProvider();

        Assert.Equal(2, await SendInScope(root, TestSecurityContexts.With("Admin"), 1));
        await Assert.ThrowsAsync<AuthorizationDeniedException>(
            () => SendInScope(root, TestSecurityContexts.With(), 1));
        Assert.Equal(6, await SendInScope(root, TestSecurityContexts.With("Admin"), 3));
    }

    [Fact]
    public async Task An_unprivileged_first_caller_does_not_deny_later_privileged_callers()
    {
        using var root = BuildPerScopeIdentityProvider();

        await Assert.ThrowsAsync<AuthorizationDeniedException>(
            () => SendInScope(root, TestSecurityContexts.With(), 1));
        Assert.Equal(4, await SendInScope(root, TestSecurityContexts.With("Admin"), 2));
    }

    [Fact]
    public async Task Concurrent_requests_are_each_authorized_with_their_own_identity()
    {
        using var root = BuildPerScopeIdentityProvider();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 64).Select(async i =>
        {
            await Task.Yield();
            var isAdmin = i % 2 == 0;
            var identity = isAdmin ? TestSecurityContexts.With("Admin") : TestSecurityContexts.With();
            try
            {
                await SendInScope(root, identity, i);
                return (IsAdmin: isAdmin, Allowed: true);
            }
            catch (AuthorizationDeniedException)
            {
                return (IsAdmin: isAdmin, Allowed: false);
            }
        }));

        Assert.All(outcomes, o => Assert.Equal(o.IsAdmin, o.Allowed));
    }

    [Fact]
    public async Task Two_containers_in_one_process_authorize_against_their_own_services()
    {
        using var adminHost = BuildFixedIdentityProvider(TestSecurityContexts.With("Admin"));
        var anonymousHost = BuildFixedIdentityProvider(TestSecurityContexts.With());

        using (var adminScope = adminHost.CreateScope())
        using (var anonymousScope = anonymousHost.CreateScope())
        {
            // Resolving IMediator runs each container's PipelineBehaviorStateActivation, the
            // anonymous host's last.
            var adminMediator = adminScope.ServiceProvider.GetRequiredService<IMediator>();
            var anonymousMediator = anonymousScope.ServiceProvider.GetRequiredService<IMediator>();

            Assert.Equal(2, await adminMediator.Send(new IntegrationTestRequest(1)));
            await Assert.ThrowsAsync<AuthorizationDeniedException>(
                async () => await anonymousMediator.Send(new IntegrationTestRequest(1)));
        }

        anonymousHost.Dispose();

        // The other container is gone; this one keeps authorizing against its own services.
        using (var scope = adminHost.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            Assert.Equal(4, await mediator.Send(new IntegrationTestRequest(2)));
        }
    }

    [Fact]
    public async Task Static_dispatch_resolves_the_identity_per_request()
    {
        // The static Mediator class has no request scope. The identity source is scoped, so the
        // behavior opens a scope per request instead of resolving it once from the root.
        var identity = new CurrentIdentity();
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddSingleton(identity);
        services.AddMediator().WithAuthorization(o =>
            o.UseSecurityContextFactory(sp => sp.GetRequiredService<CurrentIdentity>().Value));
        using var root = services.BuildServiceProvider();
        _ = root.GetRequiredService<PipelineBehaviorStateActivation>();

        identity.Value = TestSecurityContexts.With("Admin");
        Assert.Equal(2, await Mediator.Send(new IntegrationTestRequest(1)));

        identity.Value = TestSecurityContexts.With();
        await Assert.ThrowsAsync<AuthorizationDeniedException>(
            async () => await Mediator.Send(new IntegrationTestRequest(1)));

        identity.Value = TestSecurityContexts.With("Admin");
        Assert.Equal(6, await Mediator.Send(new IntegrationTestRequest(3)));
    }

    [Fact]
    public async Task The_handle_without_a_provider_authorizes_against_PipelineBehaviorServices_Current()
    {
        // IMediator.Send passes its provider to the Handle overload that takes one. The other
        // overload, which older generators call, reads it from PipelineBehaviorServices.Current
        // and must still authorize with that scope's identity, not a scope of its own.
        using var root = BuildPerScopeIdentityProvider();
        _ = root.GetRequiredService<PipelineBehaviorStateActivation>();
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestSecurityContextAccessor>().Current =
            TestSecurityContexts.With("Admin");

        PipelineBehaviorServices.Current = scope.ServiceProvider;
        var result = await AuthorizationBehavior.Handle<IntegrationTestRequest, int>(
            new IntegrationTestRequest(4), CancellationToken.None,
            static (r, _) => ValueTask.FromResult(r.Value * 2));

        Assert.Equal(8, result);
    }

    // The identity is set on the scope before the request is dispatched, as authentication
    // middleware does for an HTTP request.
    private static async Task<int> SendInScope(ServiceProvider root, ISecurityContext identity, int value)
    {
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestSecurityContextAccessor>().Current = identity;
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(new IntegrationTestRequest(value));
    }

    // ValidateScopes stays off, as it is in production outside the Development environment:
    // with it on, the container itself would reject resolving a scoped service from the root.
    private static ServiceProvider BuildPerScopeIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddScoped<TestSecurityContextAccessor>();
        services.AddTransient<IntegrationTestHandler>();
        services.AddMediator().WithAuthorization(o => o.UseAccessor<TestSecurityContextAccessor>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
    }

    private static ServiceProvider BuildFixedIdentityProvider(ISecurityContext identity)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = identity });
        services.AddTransient<IntegrationTestHandler>();
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }

    private sealed class CurrentIdentity
    {
        public ISecurityContext Value { get; set; } = AnonymousSecurityContext.Instance;
    }
}
