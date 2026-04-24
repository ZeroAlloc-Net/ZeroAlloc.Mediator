namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Marks an <see cref="ZeroAlloc.Mediator.IRequest{TResponse}"/> type so that
/// <see cref="CacheBehavior"/> caches its response for the configured duration.
/// Apply to the request type (struct or class), not to a service interface.
/// </summary>
// Inherited = false: each request type must declare [CacheResponse] independently.
// Inheriting would give subclasses identical cache keys, which is almost always wrong.
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class CacheResponseAttribute : Attribute
{
    /// <summary>Cache entry lifetime in milliseconds.</summary>
    public required int TtlMs { get; init; }

    public bool Sliding { get; init; } = false;
}
