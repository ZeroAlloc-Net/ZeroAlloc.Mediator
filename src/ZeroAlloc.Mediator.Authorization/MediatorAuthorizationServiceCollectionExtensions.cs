using System;
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
        var opts = new AuthorizationOptions(builder.Services);
        configure?.Invoke(opts);
        if (!opts.ContextSourceConfigured)
        {
            throw new InvalidOperationException(
                "WithAuthorization() requires a security-context source. Call ONE of: " +
                "UseSecurityContextFactory(sp => ...), UseAnonymousSecurityContext(), or " +
                "UseAccessor<TAccessor>() inside the configure callback.");
        }
        return builder;
    }
}
