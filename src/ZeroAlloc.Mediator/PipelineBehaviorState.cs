using System;
using System.Collections.Generic;

namespace ZeroAlloc.Mediator;

#pragma warning disable MA0048 // the contract and the type that runs it are read together
/// <summary>
/// Copies what a static pipeline behavior needs out of the app's container into the behavior's
/// static state. Bridge packages register an implementation as a singleton from their
/// <c>WithXxx()</c> method.
/// </summary>
/// <remarks>
/// Pipeline behaviors are static so the generated dispatcher can call them without allocating,
/// which leaves them no instance to inject into. Their dependencies are read from static state
/// instead, and <see cref="PipelineBehaviorStateActivation"/> fills that state from the
/// container the app actually runs on.
/// </remarks>
public interface IPipelineBehaviorStateInitializer
{
    /// <summary>Publishes this container's dependencies into the behavior's static state.</summary>
    void Initialize();
}

/// <summary>
/// Runs every registered <see cref="IPipelineBehaviorStateInitializer"/> once per container.
/// Registered as a singleton by the generated <c>services.AddMediator()</c>, and resolved by the
/// generated <c>MediatorService</c> constructor, so the first <c>IMediator</c> resolved from a
/// container initializes the pipeline behaviors against that container.
/// </summary>
/// <remarks>
/// An app that only dispatches through the static <c>Mediator</c> class never resolves
/// <c>IMediator</c>. It resolves this type once after building its container instead.
/// </remarks>
public sealed class PipelineBehaviorStateActivation
{
    /// <summary>Calls <see cref="IPipelineBehaviorStateInitializer.Initialize"/> on each initializer.</summary>
    public PipelineBehaviorStateActivation(IEnumerable<IPipelineBehaviorStateInitializer> initializers)
    {
        ArgumentNullException.ThrowIfNull(initializers);
        foreach (var initializer in initializers)
            initializer.Initialize();
    }
}
#pragma warning restore MA0048
