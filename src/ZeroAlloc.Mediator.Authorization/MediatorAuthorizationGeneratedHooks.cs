using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Internal-API surface used by source-generator-emitted code to wire the discovered
/// authorization policies into the runtime. Only called by <c>[ModuleInitializer]</c>-decorated
/// code emitted by <c>ZeroAlloc.Mediator.Authorization.Generator</c>. Calling
/// <see cref="Configure"/> manually is unsupported and may break in future versions.
/// </summary>
/// <remarks>
/// First-write-wins semantics — once <see cref="Configure"/> has been called from any
/// compilation's <c>ModuleInitializer</c>, subsequent calls are no-ops. This avoids
/// cross-assembly clobbering when multiple consuming compilations each emit a wiring
/// initializer.
/// </remarks>
public static class MediatorAuthorizationGeneratedHooks
{
    private static int s_configured;

    private static Action<IServiceCollection>? s_registerDiscoveredPolicies;
    private static Func<Type[]>? s_getPolicyTypes;
    private static Func<Type, string[]> s_getPoliciesForRequestType = static _ => Array.Empty<string>();
    private static Func<string, IServiceProvider, ZeroAlloc.Authorization.IAuthorizationPolicy>? s_resolvePolicy;

    /// <summary>
    /// Wires the source-generator-emitted lookups. Only called by <c>[ModuleInitializer]</c>-decorated
    /// code; first call wins. Repeat calls are no-ops.
    /// </summary>
    /// <param name="registerDiscoveredPolicies">
    /// Registers every discovered <c>[AuthorizationPolicy]</c> as <c>AddScoped</c>.
    /// <see langword="null"/> when no policies were discovered.
    /// </param>
    /// <param name="getPolicyTypes">
    /// All policy CLR types the generator discovered, used for eager DI validation
    /// inside <c>WithAuthorization()</c>. <see langword="null"/> when no policies were discovered.
    /// </param>
    /// <param name="getPoliciesForRequestType">
    /// Returns the policy names that apply to a request type, keyed by <see cref="Type"/>.
    /// Empty array when the request has no <c>[Authorize]</c> attributes.
    /// </param>
    /// <param name="resolvePolicy">
    /// Resolves a policy instance by name from the supplied <see cref="IServiceProvider"/>.
    /// <see langword="null"/> when no policies were discovered.
    /// </param>
    public static void Configure(
        Action<IServiceCollection>? registerDiscoveredPolicies,
        Func<Type[]>? getPolicyTypes,
        Func<Type, string[]> getPoliciesForRequestType,
        Func<string, IServiceProvider, ZeroAlloc.Authorization.IAuthorizationPolicy>? resolvePolicy)
    {
        if (getPoliciesForRequestType is null)
            throw new ArgumentNullException(nameof(getPoliciesForRequestType));

        // First-write-wins: set s_configured to 1 atomically; if it was already 1, bail.
        if (Interlocked.Exchange(ref s_configured, 1) != 0)
            return;

        s_registerDiscoveredPolicies = registerDiscoveredPolicies;
        s_getPolicyTypes = getPolicyTypes;
        s_getPoliciesForRequestType = getPoliciesForRequestType;
        s_resolvePolicy = resolvePolicy;
    }

    internal static Action<IServiceCollection>? RegisterDiscoveredPolicies => s_registerDiscoveredPolicies;

    internal static Func<Type[]>? GetPolicyTypes => s_getPolicyTypes;

    /// <remarks>
    /// The hot path inside <c>AuthorizationBehavior</c> uses a per-<c>TRequest</c> static
    /// cache (<c>RequestPolicies&lt;TRequest&gt;.Names</c>) populated by calling this delegate once
    /// per closed generic instantiation. This keeps the dispatch zero-allocation after warmup
    /// while preserving the runtime's ability to talk to user-compilation-local generated code.
    /// </remarks>
    internal static Func<Type, string[]> GetPoliciesForRequestType => s_getPoliciesForRequestType;

    internal static Func<string, IServiceProvider, ZeroAlloc.Authorization.IAuthorizationPolicy>? ResolvePolicy => s_resolvePolicy;
}
