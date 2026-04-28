using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Mediator;

internal sealed class MediatorBuilder(IServiceCollection services) : IMediatorBuilder
{
    public IServiceCollection Services { get; } = services;
}
