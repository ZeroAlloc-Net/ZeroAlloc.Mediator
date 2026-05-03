using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Resilience;

public static class MediatorResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the resilience pipeline-behavior marker.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IMediatorBuilder WithResilience(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithResilience more than once.
        if (services.Any(d => d.ServiceType == typeof(MediatorResilienceMarker)))
            return builder;

        services.AddSingleton<MediatorResilienceMarker>();

        return builder;
    }

    /// <summary>
    /// Legacy v1.x entry point. Use <see cref="WithResilience"/> on the builder returned by
    /// <c>services.AddMediator()</c> instead. Will be removed in the next major.
    /// </summary>
    [Obsolete("Use services.AddMediator().WithResilience() instead. Will be removed in the next major.", DiagnosticId = "ZAMED003")]
    public static IServiceCollection AddMediatorResilience(this IServiceCollection services)
    {
        // Equivalent to services.AddMediator().WithResilience(), but the generated AddMediator()
        // extension is emitted into consuming projects and isn't visible inside this library —
        // construct the builder directly. Consumers should still call AddMediator()
        // themselves to register IMediator; the back-compat contract here is only that the
        // resilience marker gets registered.
        new MediatorBuilder(services).WithResilience();
        return services;
    }
}
