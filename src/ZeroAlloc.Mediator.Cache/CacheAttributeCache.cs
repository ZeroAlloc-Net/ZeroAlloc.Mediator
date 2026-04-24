namespace ZeroAlloc.Mediator.Cache;

// Per-TRequest type: one reflection call total on first access, then a static read forever.
internal static class CacheAttributeCache<TRequest>
{
    internal static readonly CacheResponseAttribute? Attribute =
        (CacheResponseAttribute?)System.Attribute.GetCustomAttribute(typeof(TRequest), typeof(CacheResponseAttribute));
}
