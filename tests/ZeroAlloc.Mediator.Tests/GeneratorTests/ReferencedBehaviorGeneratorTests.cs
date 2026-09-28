using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// Behaviors shipped in a referenced assembly, the way the bridge packages ship theirs. Before
// #234 the generator only looked at the consumer's own syntax trees, so none of these reached
// the generated Send.
public class ReferencedBehaviorGeneratorTests
{
    private const string Consumer = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        public readonly record struct Ping(string Message) : IRequest<string>;
        public readonly record struct Pong(string Message) : IRequest<string>;

        public class PingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult("Pong");
        }

        public class PongHandler : IRequestHandler<Pong, string>
        {
            public ValueTask<string> Handle(Pong request, CancellationToken ct) => ValueTask.FromResult("Ping");
        }

        [PipelineBehavior(Order = 0)]
        public class LocalBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        }
        """;

    private const string HandleMethod = """
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        """;

    private static MetadataReference CompileLibrary(string source)
    {
        var compilation = CSharpCompilation.Create(
            "BridgeLibrary",
            [CSharpSyntaxTree.ParseText(source)],
            GeneratorTestHelper.BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static string Library(string body) => $$"""
        using ZeroAlloc.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Bridge;

        {{body}}
        """;

    [Fact]
    public void PublicBehavior_InReferencedAssembly_IsInlinedIntoSend()
    {
        var bridge = CompileLibrary(Library($$"""
            [PipelineBehavior(Order = -500)]
            public sealed class BridgeBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.Contains("global::Bridge.BridgeBehavior.Handle", output);
        // Order -500 runs outside the consumer's Order 0 behavior.
        var bridgeIdx = output.IndexOf("global::Bridge.BridgeBehavior.Handle", StringComparison.Ordinal);
        var localIdx = output.IndexOf("global::TestApp.LocalBehavior.Handle", StringComparison.Ordinal);
        Assert.True(bridgeIdx >= 0 && localIdx > bridgeIdx, "BridgeBehavior should wrap LocalBehavior");
    }

    [Fact]
    public void ReferencedBehavior_WithAppliesTo_OnlyWrapsItsTarget()
    {
        // A library can only scope a behavior to its own request types. None of the consumer's
        // requests match, so the behavior must not wrap any of them.
        var bridge = CompileLibrary(Library($$"""
            public readonly record struct BridgeRequest(int Id) : IRequest<int>;

            [PipelineBehavior(Order = -500, AppliesTo = typeof(BridgeRequest))]
            public sealed class ScopedBridgeBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (output, _) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        Assert.DoesNotContain("ScopedBridgeBehavior", output);
    }

    [Fact]
    public void ReferencedBehavior_OrdersAgainstLocalBehaviorsByOrder()
    {
        var bridge = CompileLibrary(Library($$"""
            [PipelineBehavior(Order = 10)]
            public sealed class InnerBridgeBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (output, _) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        var localIdx = output.IndexOf("global::TestApp.LocalBehavior.Handle", StringComparison.Ordinal);
        var bridgeIdx = output.IndexOf("global::Bridge.InnerBridgeBehavior.Handle", StringComparison.Ordinal);
        Assert.True(localIdx >= 0 && bridgeIdx > localIdx, "LocalBehavior (Order 0) should wrap InnerBridgeBehavior (Order 10)");
    }

    [Fact]
    public void InternalBehavior_InReferencedAssembly_IsIgnored()
    {
        var bridge = CompileLibrary(Library($$"""
            [PipelineBehavior(Order = -500)]
            internal sealed class HiddenBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (output, _) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        Assert.DoesNotContain("HiddenBehavior", output);
    }

    [Fact]
    public void ReferencedBehavior_WithoutMediatorMarker_IsIgnored()
    {
        // Other libraries build their own pipelines on ZeroAlloc.Pipeline. A behavior that only
        // implements the base ZeroAlloc.Pipeline.IPipelineBehavior is not a Mediator behavior.
        var bridge = CompileLibrary(Library($$"""
            [ZeroAlloc.Pipeline.PipelineBehavior(-500)]
            public sealed class ForeignBehavior : ZeroAlloc.Pipeline.IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (output, _) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        Assert.DoesNotContain("ForeignBehavior", output);
    }

    [Fact]
    public void ReferencedBehavior_SharingAnOrderWithALocalOne_ReportsZam006()
    {
        var bridge = CompileLibrary(Library($$"""
            [PipelineBehavior(Order = 0)]
            public sealed class SameOrderBehavior : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """));

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(Consumer, [bridge]);

        Assert.Contains(diagnostics, d => d.Id == "ZAM006");
    }
}
