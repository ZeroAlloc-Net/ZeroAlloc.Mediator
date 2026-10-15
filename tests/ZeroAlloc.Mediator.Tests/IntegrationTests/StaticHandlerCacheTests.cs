using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// Fixtures for the static Mediator's parameterless fallback. None of these handlers is given a
// factory, so every Send resolves them through the generated fallback.
public readonly record struct StatelessCacheProbe : IRequest<object>;

public readonly record struct StatefulFieldProbe : IRequest<int>;

public readonly record struct StatefulPropertyProbe : IRequest<int>;

public readonly record struct InheritedStateProbe : IRequest<int>;

// Static fields and constants are not instance state, so this handler is still cached.
public sealed class StatelessCacheProbeHandler : IRequestHandler<StatelessCacheProbe, object>
{
    private const int Unused = 0;
    private static int s_calls;

    public ValueTask<object> Handle(StatelessCacheProbe request, CancellationToken ct)
    {
        Interlocked.Add(ref s_calls, 1 + Unused);
        return ValueTask.FromResult<object>(this);
    }
}

public sealed class StatefulFieldProbeHandler : IRequestHandler<StatefulFieldProbe, int>
{
    private int _calls;

    public ValueTask<int> Handle(StatefulFieldProbe request, CancellationToken ct) => ValueTask.FromResult(++_calls);
}

public sealed class StatefulPropertyProbeHandler : IRequestHandler<StatefulPropertyProbe, int>
{
    private int Calls { get; set; }

    public ValueTask<int> Handle(StatefulPropertyProbe request, CancellationToken ct) => ValueTask.FromResult(++Calls);
}

public abstract class InheritedStateProbeBase
{
    protected int Calls;
}

public sealed class InheritedStateProbeHandler : InheritedStateProbeBase, IRequestHandler<InheritedStateProbe, int>
{
    public ValueTask<int> Handle(InheritedStateProbe request, CancellationToken ct) => ValueTask.FromResult(++Calls);
}

public readonly record struct FlakyConstructionProbe : IRequest<object>;

// Stateless, but its first construction fails: the failure must surface as the constructor's own
// exception, as it did before caching, and must not stop a later dispatch from succeeding.
public sealed class FlakyConstructionProbeHandler : IRequestHandler<FlakyConstructionProbe, object>
{
    private static int s_constructions;

    public FlakyConstructionProbeHandler()
    {
        if (Interlocked.Increment(ref s_constructions) == 1)
            throw new InvalidOperationException("first construction fails");
    }

    public ValueTask<object> Handle(FlakyConstructionProbe request, CancellationToken ct) => ValueTask.FromResult<object>(this);
}

public sealed class StaticHandlerCacheTests
{
    [Fact]
    public async Task StatelessHandler_WhoseConstructorThrows_PropagatesTheException_AndIsRetried()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Mediator.Send(new FlakyConstructionProbe(), CancellationToken.None).AsTask());
        Assert.Equal("first construction fails", ex.Message);

        var first = await Mediator.Send(new FlakyConstructionProbe(), CancellationToken.None);
        var second = await Mediator.Send(new FlakyConstructionProbe(), CancellationToken.None);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task StatelessHandler_IsReusedAcrossCalls()
    {
        var first = await Mediator.Send(new StatelessCacheProbe(), CancellationToken.None);
        var second = await Mediator.Send(new StatelessCacheProbe(), CancellationToken.None);

        Assert.IsType<StatelessCacheProbeHandler>(first);
        Assert.Same(first, second);
    }

    // Each call starts from a fresh instance, so the per-instance counter never passes 1.
    [Fact]
    public async Task HandlerWithInstanceField_IsCreatedPerCall()
    {
        Assert.Equal(1, await Mediator.Send(new StatefulFieldProbe(), CancellationToken.None));
        Assert.Equal(1, await Mediator.Send(new StatefulFieldProbe(), CancellationToken.None));
    }

    [Fact]
    public async Task HandlerWithAutoProperty_IsCreatedPerCall()
    {
        Assert.Equal(1, await Mediator.Send(new StatefulPropertyProbe(), CancellationToken.None));
        Assert.Equal(1, await Mediator.Send(new StatefulPropertyProbe(), CancellationToken.None));
    }

    [Fact]
    public async Task HandlerWithInheritedField_IsCreatedPerCall()
    {
        Assert.Equal(1, await Mediator.Send(new InheritedStateProbe(), CancellationToken.None));
        Assert.Equal(1, await Mediator.Send(new InheritedStateProbe(), CancellationToken.None));
    }
}
