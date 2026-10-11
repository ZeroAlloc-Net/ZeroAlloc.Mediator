namespace ZeroAlloc.Mediator.Cache;

/// <summary>
/// Marks an <see cref="ZeroAlloc.Mediator.IRequest{TResponse}"/> type so that
/// <see cref="CacheBehavior"/> caches its response for the configured duration.
/// Apply to the request type (struct or class), not to a service interface.
/// </summary>
/// <remarks>
/// The cache key is <c>"&lt;request type full name&gt;:&lt;request&gt;"</c>, where the request is
/// formatted through <see cref="IFormattable"/> when it implements it, and through
/// <see cref="object.ToString"/> otherwise, so that text has to identify the request. A record, a
/// <see cref="object.ToString"/> override or an <see cref="ISpanFormattable"/> implementation does.
/// A class or struct with instance state that has none of these formats as its type name, so all
/// its instances would share one entry; the generator reports it as ZAM010. A record prints each
/// member through the member's own <see cref="object.ToString"/>, so a member such as an array
/// or a <c>List&lt;T&gt;</c> prints as its type name; the generator reports it as ZAM011.
/// </remarks>
// Inherited = false: each request type must declare [CacheResponse] independently.
// Inheriting would give subclasses identical cache keys, which is almost always wrong.
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class CacheResponseAttribute : Attribute
{
    /// <summary>Cache entry lifetime in milliseconds.</summary>
    public required int TtlMs { get; init; }

    public bool Sliding { get; init; } = false;
}
