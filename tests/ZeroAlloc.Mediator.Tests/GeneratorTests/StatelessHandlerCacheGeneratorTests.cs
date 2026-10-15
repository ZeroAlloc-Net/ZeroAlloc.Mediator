using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// The static Mediator's fallback when no factory is registered: one cached instance for a handler
// without instance state, a new instance per call for any other handler (#328).
public class StatelessHandlerCacheGeneratorTests
{
    private const string Header = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        public readonly record struct Ping : IRequest<int>;

        """;

    private const string Cached = "_pingHandlerFactory?.Invoke() ?? (_pingHandlerInstance ?? __CreatePingHandlerInstance())";
    private const string PerCall = "_pingHandlerFactory?.Invoke() ?? new global::TestApp.PingHandler()";

    [Theory]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { BODY }")]
    [InlineData("public sealed class PingHandler : IRequestHandler<Ping, int> { private static int s_count; private const int Max = 3; BODY }")]
    [InlineData("public abstract class Base { protected static int Shared; } public class PingHandler : Base, IRequestHandler<Ping, int> { BODY }")]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { public static event Action? Changed; private static int Count { get; set; } BODY }")]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { private int Computed => 1; public PingHandler() { } BODY }")]
    public void StatelessHandler_IsCached(string handler)
    {
        var (output, generatorDiagnostics, compilationDiagnostics) = Run(handler);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(Cached, output);
        Assert.Contains("private static global::TestApp.PingHandler? _pingHandlerInstance;", output);
        Assert.Contains("return global::System.Threading.Interlocked.CompareExchange(ref _pingHandlerInstance, created, null) ?? created;", output);
        Assert.DoesNotContain(PerCall, output);
    }

    [Theory]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { private int _count; BODY }")]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { private readonly object _lock = new(); BODY }")]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { public int Count { get; set; } BODY }")]
    [InlineData("public class PingHandler : IRequestHandler<Ping, int> { public event Action? Changed; BODY }")]
    [InlineData("public abstract class Base { private int _count; } public class PingHandler : Base, IRequestHandler<Ping, int> { BODY }")]
    [InlineData("public abstract class Root { protected int Count { get; set; } } public abstract class Mid : Root { } public class PingHandler : Mid, IRequestHandler<Ping, int> { BODY }")]
    [InlineData("public class PingHandler(int seed) : IRequestHandler<Ping, int> { public PingHandler() : this(1) { } public ValueTask<int> Handle(Ping r, CancellationToken ct) => new(seed); }")]
    [InlineData("public class PingHandler : List<int>, IRequestHandler<Ping, int> { BODY }")]
    public void StatefulHandler_IsCreatedPerCall(string handler)
    {
        var (output, generatorDiagnostics, compilationDiagnostics) = Run(handler);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(PerCall, output);
        Assert.DoesNotContain("PingHandlerInstance", output);
    }

    [Fact]
    public void EveryDispatchKind_IsCached_WithOneCachePerHandlerType()
    {
        var (output, _, compilationDiagnostics) = GeneratorTestHelper.RunGeneratorAndCompile("""
            using ZeroAlloc.Mediator;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<int>;
            public readonly record struct Changed : INotification;
            [ParallelNotification]
            public readonly record struct Fanned : INotification;
            public readonly record struct Count : IStreamRequest<int>;

            public class MultiHandler : IRequestHandler<Ping, int>, INotificationHandler<Changed>
            {
                public ValueTask<int> Handle(Ping r, CancellationToken ct) => default;
                public ValueTask Handle(Changed n, CancellationToken ct) => default;
            }

            public class FannedHandler : INotificationHandler<Fanned>
            {
                public ValueTask Handle(Fanned n, CancellationToken ct) => default;
            }

            public class CountHandler : IStreamRequestHandler<Count, int>
            {
                public async IAsyncEnumerable<int> Handle(Count r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }
            """);

        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("await (_multiHandlerFactory?.Invoke() ?? (_multiHandlerInstance ?? __CreateMultiHandlerInstance())).Handle(notification, ct);", output);
        Assert.Contains("var handler = _multiHandlerFactory?.Invoke() ?? (_multiHandlerInstance ?? __CreateMultiHandlerInstance());", output);
        Assert.Contains("__TrackParallelHandler((_fannedHandlerFactory?.Invoke() ?? (_fannedHandlerInstance ?? __CreateFannedHandlerInstance())).Handle(notification, ct)", output);
        Assert.Contains("var handler = _countHandlerFactory?.Invoke() ?? (_countHandlerInstance ?? __CreateCountHandlerInstance());", output);
        Assert.Equal(2, output.Split("private static global::TestApp.MultiHandler __CreateMultiHandlerInstance()", StringSplitOptions.None).Length);
    }

    [Fact]
    public void PipelineInnermostCall_UsesCachedInstance()
    {
        var (output, _, compilationDiagnostics) = Run("""
            public class PingHandler : IRequestHandler<Ping, int> { BODY }

            [PipelineBehavior(Order = 1)]
            public sealed class Forward : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """);

        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("{ var handler = " + Cached + "; return handler.Handle(", output);
    }

    private static (string output, System.Collections.Immutable.ImmutableArray<Diagnostic> generatorDiagnostics, System.Collections.Immutable.ImmutableArray<Diagnostic> compilationDiagnostics) Run(string handler)
        => GeneratorTestHelper.RunGeneratorAndCompile(
            Header + handler.Replace("BODY", "public ValueTask<int> Handle(Ping r, CancellationToken ct) => default;", StringComparison.Ordinal));
}
