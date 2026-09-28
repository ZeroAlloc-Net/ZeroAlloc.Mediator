using ZeroAlloc.Mediator;

namespace ZeroAlloc.Mediator.Validation;

// Hands the container to the static ValidationBehavior. The first IMediator resolved from the
// container runs Initialize through PipelineBehaviorStateActivation. Disposing the container
// disposes this accessor too, which clears the state while it still holds this container, so a
// validator is never resolved from a disposed provider and another live container stays wired.
internal sealed class ValidationBehaviorAccessor : IPipelineBehaviorStateInitializer, IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    internal ValidationBehaviorAccessor(IServiceProvider serviceProvider) =>
        _serviceProvider = serviceProvider;

    public void Initialize() => ValidationBehaviorState.ServiceProvider = _serviceProvider;

    public void Dispose() =>
        Interlocked.CompareExchange(ref ValidationBehaviorState.ServiceProvider, null, _serviceProvider);
}
