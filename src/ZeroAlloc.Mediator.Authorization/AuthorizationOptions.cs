using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Configuration surface passed to the <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>
/// builder. The caller MUST pick a security-context source via one of <see cref="UseSecurityContextFactory"/>,
/// <see cref="UseAnonymousSecurityContext"/>, or <see cref="UseAccessor{TAccessor}"/>; otherwise
/// <c>WithAuthorization()</c> throws.
/// </summary>
public sealed class AuthorizationOptions
{
    internal IServiceCollection Services { get; }
    internal bool ContextSourceConfigured { get; private set; }

    internal AuthorizationOptions(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Register a scoped <see cref="ISecurityContext"/> via a per-scope factory.</summary>
    public void UseSecurityContextFactory(Func<IServiceProvider, ISecurityContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Services.TryAddScoped(factory);
        ContextSourceConfigured = true;
    }

    /// <summary>Register <see cref="AnonymousSecurityContext.Instance"/> as the singleton context.</summary>
    public void UseAnonymousSecurityContext()
    {
        Services.TryAddSingleton<ISecurityContext>(AnonymousSecurityContext.Instance);
        ContextSourceConfigured = true;
    }

    /// <summary>
    /// Resolve <see cref="ISecurityContext"/> from a host-supplied <typeparamref name="TAccessor"/>
    /// (e.g. an ASP.NET <c>HttpContextAccessor</c>-backed implementation).
    /// </summary>
    public void UseAccessor<TAccessor>() where TAccessor : ISecurityContextAccessor
    {
        Services.TryAddScoped<ISecurityContext>(sp => sp.GetRequiredService<TAccessor>().Current);
        ContextSourceConfigured = true;
    }
}
