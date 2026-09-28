using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Telemetry;

/// <summary>
/// Fluent <see cref="IMediatorBuilder"/> extensions for OpenTelemetry instrumentation.
/// </summary>
public static class MediatorTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Marker entry point for fluent composition: <c>services.AddMediator().WithTelemetry()</c>.
    /// Telemetry is wired automatically by package reference — the Mediator source generator
    /// finds <see cref="TelemetryBehavior"/> in this referenced assembly and puts it in the pipeline.
    /// This method exists for fluent-API consistency with <c>WithCache()</c>, <c>WithValidation()</c>,
    /// and <c>WithResilience()</c>.
    /// </summary>
    public static IMediatorBuilder WithTelemetry(this IMediatorBuilder builder) => builder;
}
