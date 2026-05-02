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

public class OpenGenericNotificationHandler<T> : INotificationHandler<T> where T : INotification
{
    public ValueTask Handle(T notification, CancellationToken ct) => ValueTask.CompletedTask;
}

public readonly record struct MultiQuery(int Id) : IRequest<int>;
public readonly record struct MultiEvent(int Id) : INotification;

public class MultiInterfaceHandler : IRequestHandler<MultiQuery, int>, INotificationHandler<MultiEvent>
{
    public ValueTask<int> Handle(MultiQuery request, CancellationToken ct) => ValueTask.FromResult(request.Id);
    public ValueTask Handle(MultiEvent notification, CancellationToken ct) => ValueTask.CompletedTask;
}

// Distinct request type so the generator (ZAM002) does not see two handlers
// for the same request type when both ScanPingHandler and InternalScanHandler
// are present in the same test assembly. Request type is public so the generator
// can emit public Send-method signatures referencing it; the handler is internal
// to exercise the scanner's IsPublic filter.
public readonly record struct InternalScanRequest(string Message) : IRequest<string>;

internal class InternalScanHandler : IRequestHandler<InternalScanRequest, string>
{
    public ValueTask<string> Handle(InternalScanRequest request, CancellationToken ct) => ValueTask.FromResult("internal");
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

    [Fact]
    public void Skips_OpenGeneric_HandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(OpenGenericNotificationHandler<>).Assembly);

        Assert.DoesNotContain(services,
            d => d.ServiceType == typeof(OpenGenericNotificationHandler<>));
    }

    [Fact]
    public void Handler_ImplementingMultipleInterfaces_RegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(MultiInterfaceHandler).Assembly);

        var count = services.Count(d => d.ServiceType == typeof(MultiInterfaceHandler));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Skips_InternalHandlerTypes()
    {
        var services = new ServiceCollection();
        services.AddMediator()
            .RegisterHandlersFromAssembly(typeof(InternalScanHandler).Assembly);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(InternalScanHandler));
    }
}
