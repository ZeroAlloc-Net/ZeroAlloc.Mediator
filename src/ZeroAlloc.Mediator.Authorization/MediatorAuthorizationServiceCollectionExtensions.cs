using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Authorization;

/// <summary>
/// Hooks the authorization pipeline into <see cref="IMediatorBuilder"/>. The configure
/// callback MUST select a security-context source — see <see cref="AuthorizationOptions"/>.
/// </summary>
public static class MediatorAuthorizationServiceCollectionExtensions
{
    public static IMediatorBuilder WithAuthorization(
        this IMediatorBuilder builder,
        Action<AuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var opts = new AuthorizationOptions(services);
        configure?.Invoke(opts);
        if (!opts.ContextSourceConfigured)
        {
            throw new InvalidOperationException(
                "WithAuthorization() requires a security-context source. Call ONE of: " +
                "UseSecurityContextFactory(sp => ...), UseAnonymousSecurityContext(), or " +
                "UseAccessor<TAccessor>() inside the configure callback.");
        }

        // Idempotency guard: WithAuthorization may be called more than once safely.
        if (services.Any(d => d.ServiceType == typeof(AuthorizationBehaviorAccessor)))
            return builder;

        // Auto-register discovered [AuthorizationPolicy] types. This goes through the
        // generator-emitted hook (populated via [ModuleInitializer] in the user's compilation).
        if (opts.AutoRegisterDiscoveredPolicies)
        {
            MediatorAuthorizationGeneratedHooks.RegisterDiscoveredPolicies?.Invoke(services);
        }

        // Eager validation: after policy registration, walk the generator-discovered policy types
        // and verify each is resolvable. Catches "[AuthorizationPolicy] declared but not in DI"
        // at startup rather than at first-request time.
        ValidatePoliciesAreRegistered(services);

        // Stash the IServiceProvider into the behavior's static state on first IServiceProvider
        // construction. Singleton service triggers state init once per provider build.
        services.AddSingleton(sp => new AuthorizationBehaviorAccessor(sp));

        return builder;
    }

    private static void ValidatePoliciesAreRegistered(IServiceCollection services)
    {
        var getTypes = MediatorAuthorizationGeneratedHooks.GetPolicyTypes;
        if (getTypes is null) return; // No policies discovered in the consuming compilation.

        var declaredTypes = getTypes();
        if (declaredTypes is null || declaredTypes.Length == 0) return;

        var registered = new HashSet<Type>();
        foreach (var d in services)
        {
            registered.Add(d.ServiceType);
        }

        List<Type>? missing = null;
        foreach (var t in declaredTypes)
        {
            if (!registered.Contains(t))
            {
                missing ??= new List<Type>();
                missing.Add(t);
            }
        }

        if (missing is not null)
        {
            var sb = new StringBuilder();
            sb.Append("WithAuthorization(): the following [AuthorizationPolicy] types were discovered ");
            sb.Append("but not registered in IServiceCollection. Either enable AutoRegisterDiscoveredPolicies ");
            sb.Append("(default) or register them manually before calling WithAuthorization(): ");
            for (var i = 0; i < missing.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(missing[i].FullName ?? missing[i].Name);
            }
            sb.Append('.');
            throw new InvalidOperationException(sb.ToString());
        }
    }
}
