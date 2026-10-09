using System;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Overrides the default lifetime used by
/// <c>services.AddMediator()</c> and <c>AddMediator(ServiceLifetime)</c> for the decorated handler.
/// Without this attribute, the lifetime supplied to <c>AddMediator(ServiceLifetime)</c>
/// (default <see cref="ServiceLifetime.Transient"/>) applies; the attribute always wins.
/// </summary>
/// <remarks>
/// The attribute must be applied directly to the concrete handler type;
/// it is not inherited from base classes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerLifetimeAttribute : Attribute
{
    public HandlerLifetimeAttribute(ServiceLifetime lifetime) => Lifetime = lifetime;
    public ServiceLifetime Lifetime { get; }
}
