using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator;

/// <summary>
/// Default <see cref="IMediatorBuilder"/> implementation. Constructed by the generated
/// <c>services.AddMediator()</c> extension; consumers should call <c>services.AddMediator()</c>
/// rather than instantiating this directly.
/// </summary>
public sealed class MediatorBuilder(IServiceCollection services) : IMediatorBuilder
{
    public IServiceCollection Services { get; } = services;
}
