using System;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;

namespace ZeroAlloc.Mediator.Authorization.Tests;

public class WithAuthorizationTests
{
    [Fact]
    public void UseAnonymousSecurityContext_RegistersSingleton()
    {
        var services = new ServiceCollection();
        services.AddMediator().WithAuthorization(o => o.UseAnonymousSecurityContext());
        using var sp = services.BuildServiceProvider();

        var ctx1 = sp.GetRequiredService<ISecurityContext>();
        var ctx2 = sp.GetRequiredService<ISecurityContext>();

        Assert.Same(AnonymousSecurityContext.Instance, ctx1);
        Assert.Same(ctx1, ctx2);
    }

    [Fact]
    public void UseSecurityContextFactory_ResolvesFromFactory()
    {
        var services = new ServiceCollection();
        var marker = new TestSecurityContext("user-42");
        services.AddMediator().WithAuthorization(o => o.UseSecurityContextFactory(_ => marker));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var ctx = scope.ServiceProvider.GetRequiredService<ISecurityContext>();
        Assert.Same(marker, ctx);
    }

    [Fact]
    public void UseAccessor_ResolvesViaAccessor()
    {
        var services = new ServiceCollection();
        var ctx = new TestSecurityContext("user-99");
        services.AddScoped<ISecurityContextAccessor>(_ => new StubAccessor(ctx));
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<ISecurityContext>();
        Assert.Same(ctx, resolved);
    }

    [Fact]
    public void WithAuthorization_WithoutContextSource_Throws()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediator().WithAuthorization());
        Assert.Contains("UseAnonymousSecurityContext", ex.Message, StringComparison.Ordinal);
    }

    private sealed class TestSecurityContext(string id) : ISecurityContext
    {
        public string Id { get; } = id;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string> Claims { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private sealed class StubAccessor(ISecurityContext current) : ISecurityContextAccessor
    {
        public ISecurityContext Current { get; } = current;
    }
}
