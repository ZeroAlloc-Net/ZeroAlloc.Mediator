namespace ZeroAlloc.Mediator;

/// <summary>
/// Marks a class as a ZeroAlloc.Mediator pipeline behavior.
/// Identical API to <see cref="ZeroAlloc.Pipeline.PipelineBehaviorAttribute"/> —
/// kept here for backward compatibility so existing consumers need no code changes.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorAttribute(int order = 0)
    : ZeroAlloc.Pipeline.PipelineBehaviorAttribute(order)
{
    /// <summary>
    /// Whether the behavior reads <see cref="PipelineBehaviorServices.Current"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated <c>IMediator.Send</c> sets <see cref="PipelineBehaviorServices.Current"/>
    /// only when a behavior in the request's pipeline may read it. Setting it is an
    /// <see cref="System.Threading.AsyncLocal{T}"/> write, which allocates on every
    /// <c>Send</c>. Set this to <see langword="false"/> on a behavior that never reads it, so a
    /// pipeline made only of such behaviors skips that write.
    /// </para>
    /// <para>
    /// A behavior that needs the request's services can instead declare a second
    /// <c>Handle</c> overload that takes them as an argument:
    /// <c>Handle&lt;TRequest, TResponse&gt;(TRequest request, IServiceProvider services,
    /// CancellationToken ct, Func&lt;TRequest, CancellationToken, ValueTask&lt;TResponse&gt;&gt; next)</c>.
    /// The generated <c>IMediator.Send</c> calls that overload with the provider it was resolved
    /// from, and the behavior then needs no <see cref="PipelineBehaviorServices.Current"/> either.
    /// The static <c>Mediator</c> class, which has no provider, still calls the overload without
    /// one.
    /// </para>
    /// <para>
    /// The value is read by the source generator at compile time, never at run time.
    /// </para>
    /// </remarks>
    public bool UsesPipelineBehaviorServices { get; set; } = true;
}
