namespace ZeroAlloc.Mediator.Resilience;

/// <summary>
/// Sentinel singleton used by the idempotency guard in
/// <see cref="MediatorResilienceServiceCollectionExtensions.WithResilience"/>.
/// Not intended for direct consumption.
/// </summary>
internal sealed class MediatorResilienceMarker;
