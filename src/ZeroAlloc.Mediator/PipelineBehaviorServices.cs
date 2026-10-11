using System;
using System.Threading;

namespace ZeroAlloc.Mediator;

/// <summary>
/// The service provider of the request that is running through the pipeline. Pipeline behaviors
/// are static, so this is how they reach the services of the request they handle.
/// </summary>
/// <remarks>
/// <para>
/// The generated <c>IMediator</c> sets <see cref="Current"/> to its own provider at the start of
/// every <c>Send</c> that has pipeline behaviors. <c>IMediator</c> is transient, so when it is
/// resolved from a scope, such as an ASP.NET Core request's, that provider is the scope's, and a
/// behavior resolves scoped services, such as the caller's security context, from the request
/// that is being dispatched.
/// </para>
/// <para>
/// The value flows with the async call: it is visible to every behavior and handler of that
/// <c>Send</c>, including after an <c>await</c>, and it reverts when <c>Send</c> returns. Two
/// requests dispatched concurrently, or by two containers in one process, each see their own.
/// </para>
/// <para>
/// It is <see langword="null"/> when the request was not dispatched through <c>IMediator</c>, for
/// example through the static <c>Mediator</c> class. A behavior then has no request scope.
/// </para>
/// </remarks>
public static class PipelineBehaviorServices
{
    private static readonly AsyncLocal<IServiceProvider?> s_current = new();

    /// <summary>
    /// The provider of the request being dispatched, or <see langword="null"/> outside an
    /// <c>IMediator</c> dispatch.
    /// </summary>
    /// <remarks>
    /// Setting it inside an <see langword="async"/> method is undone when that method returns, as
    /// for any <see cref="AsyncLocal{T}"/>. Setting it to the value it already has is free.
    /// </remarks>
    public static IServiceProvider? Current
    {
        get => s_current.Value;
        set
        {
            // An AsyncLocal write copies the execution context. A nested Send on the same scope,
            // a handler sending another request, keeps the value and skips that copy.
            if (!ReferenceEquals(s_current.Value, value))
                s_current.Value = value;
        }
    }
}
