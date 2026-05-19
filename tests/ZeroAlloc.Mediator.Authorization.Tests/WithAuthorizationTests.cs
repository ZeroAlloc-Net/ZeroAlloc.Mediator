using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Verifies the wiring contract of services.AddMediator().WithAuthorization(...):
//   1. A security-context source MUST be configured (UseAnonymous / UseFactory / UseAccessor).
//   2. The D3 guard fires unless services.AddZeroAllocAuthorization() was called first.
//   3. Multiple WithAuthorization() calls are idempotent.
public class WithAuthorizationTests
{
    [Fact]
    public void UseAnonymousSecurityContext_RegistersSingleton()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
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
        services.AddZeroAllocAuthorization();
        var marker = new TestSecurityContext("user-42",
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
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
        services.AddZeroAllocAuthorization();
        var ctx = new TestSecurityContext("user-99",
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<ISecurityContext>();
        Assert.Same(ctx, resolved);
    }

    [Fact]
    public void RequiresSecurityContextSource_ThrowsInvalidOperation()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediator().WithAuthorization());

        Assert.Contains("UseAnonymousSecurityContext", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Idempotency_DoubleRegistration_NoOps()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        var builder = services.AddMediator();
        builder.WithAuthorization(o => o.UseAnonymousSecurityContext());
        builder.WithAuthorization(o => o.UseAnonymousSecurityContext()); // second call must be a no-op

        // The accessor singleton is the side effect WithAuthorization registers. Exactly one
        // accessor descriptor proves the second call short-circuited.
        var accessorRegs = services.Count(d => string.Equals(d.ServiceType.FullName, "ZeroAlloc.Mediator.Authorization.AuthorizationBehaviorAccessor", StringComparison.Ordinal));
        Assert.Equal(1, accessorRegs);
    }

    // ---- D3 missing-registration guard ----

    [Fact]
    public void WithoutAddZeroAllocAuthorization_ThrowsClearError()
    {
        var services = new ServiceCollection();
        // NO services.AddZeroAllocAuthorization();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMediator().WithAuthorization(o => o.UseAnonymousSecurityContext()));

        Assert.Contains("services.AddZeroAllocAuthorization()", ex.Message, StringComparison.Ordinal);
        Assert.Contains("before 'services.AddMediator(...)'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithAddZeroAllocAuthorization_FirstSucceeds()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddMediator().WithAuthorization(o => o.UseAnonymousSecurityContext());
        // No throw expected
        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IMediator>());
    }

    [Fact]
    public void WithAddZeroAllocAuthorization_WrongOrder_Throws()
    {
        var services = new ServiceCollection();
        // WRONG ORDER: AddMediator().WithAuthorization() called BEFORE AddZeroAllocAuthorization()
        // should throw inside WithAuthorization — and AddZeroAllocAuthorization later doesn't
        // retroactively fix the failure.
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            services.AddMediator().WithAuthorization(o => o.UseAnonymousSecurityContext());
            services.AddZeroAllocAuthorization();  // too late
        });

        Assert.Contains("services.AddZeroAllocAuthorization()", ex.Message, StringComparison.Ordinal);
    }
}
