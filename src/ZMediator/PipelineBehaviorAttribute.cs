namespace ZMediator;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PipelineBehaviorAttribute(int order = 0) : Attribute
{
    public int Order { get; } = order;
    public Type? AppliesTo { get; set; }
}
