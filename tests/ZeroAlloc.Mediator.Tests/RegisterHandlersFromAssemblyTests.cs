using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Tests;

public readonly record struct ScanPing(string Message) : IRequest<string>;

public class ScanPingHandler : IRequestHandler<ScanPing, string>
{
    public ValueTask<string> Handle(ScanPing request, CancellationToken ct)
        => ValueTask.FromResult("pong");
}

public readonly record struct ScanEvent(int Id) : INotification;

public class ScanEventHandler : INotificationHandler<ScanEvent>
{
    public ValueTask Handle(ScanEvent notification, CancellationToken ct)
        => ValueTask.CompletedTask;
}

// Distinct request type so the generator (ZAM002) does not see two handlers
// for the same request type when both ScanPingHandler and ScopedHandlerWithAttribute
// are present in the same test assembly.
public readonly record struct ScopedScanRequest(int Value) : IRequest<int>;

[HandlerLifetime(ServiceLifetime.Scoped)]
public class ScopedHandlerWithAttribute : IRequestHandler<ScopedScanRequest, int>
{
    public ValueTask<int> Handle(ScopedScanRequest request, CancellationToken ct)
        => ValueTask.FromResult(0);
}

public class RegisterHandlersFromAssemblyTests
{
    [Fact]
    public void Registers_RequestHandlers_AsTransient_ByDefault()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScanPingHandler).Assembly);

        var descriptor = services.Single(d => d.ServiceType == typeof(ScanPingHandler));
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Fact]
    public void Registers_NotificationHandlers()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScanEventHandler).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(ScanEventHandler));
    }

    [Fact]
    public void Honors_HandlerLifetime_Attribute_OverDefault()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScopedHandlerWithAttribute).Assembly);

        var descriptor = services.Single(d => d.ServiceType == typeof(ScopedHandlerWithAttribute));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void DefaultLifetime_Override_AppliesWhenNoAttribute()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScanPingHandler).Assembly, ServiceLifetime.Singleton);

        var descriptor = services.Single(d => d.ServiceType == typeof(ScanPingHandler));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void RegisterHandlersFromAssemblies_RegistersAllProvided()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssemblies(
                typeof(ScanPingHandler).Assembly,
                typeof(RegisterHandlersFromAssemblyTests).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(ScanPingHandler));
    }

    [Fact]
    public void Idempotent_DoubleRegistration_DoesNotDuplicate()
    {
        var services = new ServiceCollection();
        var asm = typeof(ScanPingHandler).Assembly;
        services.AddMediator().RegisterHandlersFromAssembly(asm);
        services.AddMediator().RegisterHandlersFromAssembly(asm);

        var count = services.Count(d => d.ServiceType == typeof(ScanPingHandler));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Returns_BuilderForChaining()
    {
        var services = new ServiceCollection();
        var builder = services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(ScanPingHandler).Assembly);

        Assert.NotNull(builder);
        Assert.IsAssignableFrom<IMediatorBuilder>(builder);
    }
}
