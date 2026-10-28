using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// Pipeline behaviors do not wrap CreateStream. A behavior that declares a public static
// CheckStream method is called by CreateStream before the stream handler instead, so
// authorization and validation cover stream requests (#387). A request no behavior checks keeps
// the plain CreateStream, with nothing added to it.
public class StreamCheckGeneratorTests
{
    private const string Handler = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        public readonly record struct CountRequest(int Max) : IStreamRequest<int>;

        public class CountHandler : IStreamRequestHandler<CountRequest, int>
        {
            public async IAsyncEnumerable<int> Handle(
                CountRequest request,
                [EnumeratorCancellation] CancellationToken ct)
            {
                for (var i = 0; i < request.Max; i++)
                    yield return i;
            }
        }
        """;

    private const string SendHandle = """
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        """;

    private const string Check = """
            public static ValueTask CheckStream<TRequest>(
                TRequest request, IServiceProvider? services, CancellationToken ct)
                => ValueTask.CompletedTask;
        """;

    private static string Behavior(string name, int order, string body) => $$"""

        [PipelineBehavior(Order = {{order}})]
        public class {{name}} : IPipelineBehavior
        {
        {{body}}
        }
        """;

    [Fact]
    public void A_stream_without_a_checking_behavior_is_emitted_as_before()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            Handler + Behavior("PlainBehavior", 0, SendHandle));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain("__CheckedStream", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckStream", output, StringComparison.Ordinal);
        Assert.Contains("return handler.Handle(request, ct);", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_checking_behavior_runs_before_the_handler_on_the_static_Mediator()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            Handler + Behavior("GuardBehavior", 0, SendHandle + "\n" + Check));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var staticPart = output[..output.IndexOf("partial class MediatorService", StringComparison.Ordinal)];
        Assert.Contains("return __CheckedStream(handler, request, ct);", staticPart, StringComparison.Ordinal);
        var check = staticPart.IndexOf("GuardBehavior.CheckStream<global::TestApp.CountRequest>(request, null, ct)", StringComparison.Ordinal);
        var handle = staticPart.IndexOf("handler.Handle(request, ct).WithCancellation(ct)", StringComparison.Ordinal);
        Assert.True(check >= 0, "the check is emitted");
        Assert.True(handle > check, "the handler is called only after the check");
    }

    [Fact]
    public void MediatorService_hands_its_provider_to_the_check()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            Handler + Behavior("GuardBehavior", 0, SendHandle + "\n" + Check));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var service = output[output.IndexOf("partial class MediatorService", StringComparison.Ordinal)..];
        Assert.Contains("GuardBehavior.CheckStream<global::TestApp.CountRequest>(request, _services, ct)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_run_in_pipeline_order()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            Handler
            + Behavior("LaterGuard", 10, SendHandle + "\n" + Check)
            + Behavior("EarlierGuard", -10, SendHandle + "\n" + Check));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var earlier = output.IndexOf("EarlierGuard.CheckStream", StringComparison.Ordinal);
        var later = output.IndexOf("LaterGuard.CheckStream", StringComparison.Ordinal);
        Assert.True(earlier >= 0 && later > earlier);
    }

    [Fact]
    public void The_checked_stream_compiles()
    {
        var (_, generatorDiagnostics, compilationDiagnostics) = GeneratorTestHelper.RunGeneratorAndCompile(
            Handler + Behavior("GuardBehavior", 0, SendHandle + "\n" + Check));

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void A_CheckStream_of_the_wrong_shape_is_not_called()
    {
        var wrong = """
                public static ValueTask CheckStream<TRequest>(TRequest request, CancellationToken ct)
                    => ValueTask.CompletedTask;
            """;
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(
            Handler + Behavior("WrongBehavior", 0, SendHandle + "\n" + wrong));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain("__CheckedStream", output, StringComparison.Ordinal);
    }
}
