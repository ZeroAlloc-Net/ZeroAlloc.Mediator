namespace ZeroAlloc.Mediator.Resilience;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class MediatorCircuitBreakerAttribute : Attribute
{
    public int MaxFailures { get; init; } = 5;
    public int ResetMs { get; init; } = 1_000;
    public int HalfOpenProbes { get; init; } = 1;
}
