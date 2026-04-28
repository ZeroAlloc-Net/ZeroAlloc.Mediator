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
}
