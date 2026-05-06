using System;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Static delegate hooks populated at module-initialization time by source-generator-emitted
/// code in the consuming compilation. The runtime sub-package can't reference the generated
/// <c>GeneratedAuthorizationLookup</c> / <c>GeneratedAuthorizationDIExtensions</c> types
/// directly because they live in the user's compilation. Instead the generator emits a
/// <c>[ModuleInitializer]</c> in the user's assembly that wires these delegates.
/// </summary>
public static class MediatorAuthorizationGeneratedHooks
{
    /// <summary>
    /// Registers every discovered <c>[AuthorizationPolicy]</c> as <c>AddScoped</c>.
    /// Set by the generator's emitted <c>[ModuleInitializer]</c>; null when no policies were discovered.
    /// </summary>
    public static Action<IServiceCollection>? RegisterDiscoveredPolicies { get; set; }

    /// <summary>
    /// All policy CLR types the generator discovered, used for eager DI validation
    /// inside <c>WithAuthorization()</c>. Set by the generator's emitted <c>[ModuleInitializer]</c>;
    /// null when no policies were discovered.
    /// </summary>
    public static Func<Type[]>? GetPolicyTypes { get; set; }

    /// <summary>
    /// Returns the policy names that apply to a request type, keyed by <see cref="Type"/>.
    /// Empty array when the request has no <c>[Authorize]</c> attributes or when the generator
    /// did not emit (no policies in the consuming compilation). Set by the generator's emitted
    /// <c>[ModuleInitializer]</c>; defaults to a no-op returning an empty array.
    /// </summary>
    /// <remarks>
    /// The hot path inside <c>AuthorizationBehavior</c> uses a per-<c>TRequest</c> static
    /// cache (<c>RequestPolicies&lt;TRequest&gt;.Names</c>) populated by calling this delegate once
    /// per closed generic instantiation. This keeps the dispatch zero-allocation after warmup
    /// while preserving the runtime's ability to talk to user-compilation-local generated code.
    /// </remarks>
    public static Func<Type, string[]> GetPoliciesForRequestType { get; set; } = static _ => Array.Empty<string>();

    /// <summary>
    /// Resolves a policy instance by name from the supplied <see cref="IServiceProvider"/>.
    /// Set by the generator's emitted <c>[ModuleInitializer]</c>; null when no policies were discovered.
    /// </summary>
    public static Func<string, IServiceProvider, ZeroAlloc.Authorization.IAuthorizationPolicy>? ResolvePolicy { get; set; }
}
