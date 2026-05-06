using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Configures the security-context source for <see cref="MediatorAuthorizationServiceCollectionExtensions.WithAuthorization"/>.
/// Call <b>exactly one</b> of <see cref="UseSecurityContextFactory"/>,
/// <see cref="UseAnonymousSecurityContext"/>, or <see cref="UseAccessor{TAccessor}"/>.
/// All three use <c>TryAddScoped</c>/<c>TryAddSingleton</c> internally — registrations made BEFORE
/// <c>WithAuthorization</c> win, and only the first <c>Use*</c> call's registration takes effect.
/// </summary>
public sealed class AuthorizationOptions
{
    internal IServiceCollection Services { get; }
    internal bool ContextSourceConfigured { get; private set; }

    /// <summary>
    /// When <c>true</c> (default), <c>WithAuthorization()</c> auto-registers every discovered
    /// <c>[AuthorizationPolicy]</c> as <c>AddScoped&lt;TPolicy&gt;()</c> via the generator-emitted
    /// hook. Set to <c>false</c> to opt out and register policies manually.
    /// </summary>
    public bool AutoRegisterDiscoveredPolicies { get; set; } = true;

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
    /// Resolve <see cref="ISecurityContext"/> through a user-provided accessor.
    /// </summary>
    /// <remarks>
    /// <para><b>The accessor type must be registered separately.</b> Example:</para>
    /// <code>
    /// services.AddScoped&lt;MyAccessor&gt;();          // user registers the accessor
    /// services.AddMediator()
    ///         .WithAuthorization(opts =&gt; opts.UseAccessor&lt;MyAccessor&gt;());
    /// </code>
    /// <para>Mediator.Authorization will resolve <see cref="ISecurityContext"/> through it
    /// per-scope (each request gets a fresh value via <c>accessor.Current</c>).</para>
    /// </remarks>
    public void UseAccessor<TAccessor>() where TAccessor : ISecurityContextAccessor
    {
        Services.TryAddScoped<ISecurityContext>(sp => sp.GetRequiredService<TAccessor>().Current);
        ContextSourceConfigured = true;
    }
}
