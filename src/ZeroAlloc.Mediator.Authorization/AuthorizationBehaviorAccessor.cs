using System;

namespace ZeroAlloc.Mediator.Authorization;

#pragma warning disable MA0048 // file groups behavior-internal accessor + state
internal static class AuthorizationBehaviorState
{
    internal static volatile IServiceProvider? ServiceProvider;
}

internal sealed class AuthorizationBehaviorAccessor
{
    internal AuthorizationBehaviorAccessor(IServiceProvider serviceProvider) =>
        AuthorizationBehaviorState.ServiceProvider = serviceProvider;
}
#pragma warning restore MA0048
