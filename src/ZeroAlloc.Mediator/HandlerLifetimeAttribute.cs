using System;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Overrides the default lifetime used by
/// <c>services.AddMediator().RegisterHandlersFromAssembly(...)</c> for the decorated handler.
/// Without this attribute, the lifetime supplied to <c>RegisterHandlersFromAssembly</c>
/// (default <see cref="ServiceLifetime.Transient"/>) applies.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerLifetimeAttribute : Attribute
{
    public HandlerLifetimeAttribute(ServiceLifetime lifetime) => Lifetime = lifetime;
    public ServiceLifetime Lifetime { get; }
}
