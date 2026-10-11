namespace ZeroAlloc.Mediator.Cache;

// Per-TRequest type: the type's name in its cache keys, read once.
internal static class CacheKeyPrefix<TRequest>
{
    internal static readonly string Value = typeof(TRequest).FullName ?? typeof(TRequest).Name;
}
