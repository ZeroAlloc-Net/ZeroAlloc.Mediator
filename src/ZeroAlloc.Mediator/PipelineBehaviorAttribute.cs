namespace ZeroAlloc.Mediator;

/// <summary>
/// Marks a class as a ZeroAlloc.Mediator pipeline behavior.
/// Identical API to <see cref="ZeroAlloc.Pipeline.PipelineBehaviorAttribute"/> —
/// kept here for backward compatibility so existing consumers need no code changes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorAttribute(int order = 0)
    : ZeroAlloc.Pipeline.PipelineBehaviorAttribute(order);
