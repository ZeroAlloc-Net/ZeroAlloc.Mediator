namespace ZeroAlloc.Mediator.Validation;

internal sealed class ValidationBehaviorAccessor
{
    internal ValidationBehaviorAccessor(IServiceProvider serviceProvider) =>
        ValidationBehaviorState.ServiceProvider = serviceProvider;
}
