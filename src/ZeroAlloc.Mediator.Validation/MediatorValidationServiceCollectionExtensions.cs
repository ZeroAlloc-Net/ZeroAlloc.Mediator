using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Validation;

public static class MediatorValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the validation pipeline-behavior accessor.
    /// Idempotent — safe to call more than once.
    /// </summary>
    public static IMediatorBuilder WithValidation(this IMediatorBuilder builder)
    {
        var services = builder.Services;

        // Idempotency guard — safe to call WithValidation more than once.
        if (services.Any(d => d.ServiceType == typeof(ValidationBehaviorAccessor)))
            return builder;

        services.AddSingleton(sp => new ValidationBehaviorAccessor(sp));

        return builder;
    }

    /// <summary>
    /// Legacy v1.x entry point. Use <see cref="WithValidation"/> on the builder returned by
    /// <c>services.AddMediator()</c> instead. Will be removed in the next major.
    /// </summary>
    [Obsolete("Use services.AddMediator().WithValidation() instead. Will be removed in the next major.", DiagnosticId = "ZAMED002")]
    public static IServiceCollection AddMediatorValidation(this IServiceCollection services)
    {
        // Equivalent to services.AddMediator().WithValidation(), but the generated AddMediator()
        // extension is emitted into consuming projects and isn't visible inside this library —
        // call IMediatorBuilder.Create directly. Consumers should still call AddMediator()
        // themselves to register IMediator; the back-compat contract here is only that the
        // validation accessor gets registered.
        IMediatorBuilder.Create(services).WithValidation();
        return services;
    }
}
