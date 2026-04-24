namespace ZeroAlloc.Mediator.Resilience;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class MediatorRetryAttribute : Attribute
{
    public int MaxAttempts { get; init; } = 3;
    public int BackoffMs { get; init; } = 200;
    public bool Jitter { get; init; } = false;
    public int PerAttemptTimeoutMs { get; init; } = 0;
}
