using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Fluent builder returned by <c>services.AddMediator()</c>. Bridge packages
/// (<c>ZeroAlloc.Mediator.Cache</c>, <c>.Validation</c>, <c>.Resilience</c>,
/// <c>.Telemetry</c>) extend this interface with <c>WithXxx</c> registration helpers.
/// </summary>
public interface IMediatorBuilder
{
    IServiceCollection Services { get; }

    /// <summary>
    /// Creates an <see cref="IMediatorBuilder"/> backed by <paramref name="services"/>.
    /// Used by the generated <c>services.AddMediator()</c> extension; consumers should
    /// call <c>services.AddMediator()</c> rather than this factory directly.
    /// </summary>
    public static IMediatorBuilder Create(IServiceCollection services)
        => new MediatorBuilder(services);
}
