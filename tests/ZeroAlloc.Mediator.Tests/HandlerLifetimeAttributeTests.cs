using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Tests;

public class HandlerLifetimeAttributeTests
{
    [Fact]
    public void Attribute_StoresLifetime()
    {
        var attr = new HandlerLifetimeAttribute(ServiceLifetime.Scoped);
        Assert.Equal(ServiceLifetime.Scoped, attr.Lifetime);
    }

    [Fact]
    public void Attribute_TargetsClassesOnly()
    {
        var usage = typeof(HandlerLifetimeAttribute)
            .GetCustomAttributes(typeof(System.AttributeUsageAttribute), false)
            .Cast<System.AttributeUsageAttribute>()
            .Single();
        Assert.Equal(System.AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
