namespace ZeroAlloc.Mediator.Resilience;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class MediatorTimeoutAttribute : Attribute
{
    public required int Ms { get; init; }
}
