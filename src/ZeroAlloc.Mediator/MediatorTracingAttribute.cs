using System;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Configures the trace spans the generated <c>Send</c>, <c>Publish</c> and <c>CreateStream</c>
/// open on the <c>ZeroAlloc.Mediator</c> activity source, for the assembly it is applied to.
/// </summary>
/// <remarks>
/// The source generator reads this attribute at compile time, so a setting costs nothing at run
/// time. It applies to the dispatch code generated into the assembly that carries it, through both
/// the static <c>Mediator</c> and the injected <c>IMediator</c>.
/// <code>
/// [assembly: MediatorTracing(ExceptionDescription = false)]
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class MediatorTracingAttribute : Attribute
{
    /// <summary>
    /// Whether a span ended by an exception takes the exception's message as its status description.
    /// Defaults to true. False sets the <see cref="System.Diagnostics.ActivityStatusCode.Error"/>
    /// status with no description.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An exception message can carry request or response content, such as a prompt echoed back in
    /// a provider's error, and every listener on the <c>ZeroAlloc.Mediator</c> source receives it.
    /// Set this to false when the assembly's telemetry must not contain it.
    /// </para>
    /// <para>
    /// The exception's full type name is set as <c>error.type</c> either way, so the failure stays
    /// classifiable. This matches <c>ExceptionDescription</c> on ZeroAlloc.Telemetry's <c>[Trace]</c>.
    /// </para>
    /// </remarks>
    public bool ExceptionDescription { get; set; } = true;
}
