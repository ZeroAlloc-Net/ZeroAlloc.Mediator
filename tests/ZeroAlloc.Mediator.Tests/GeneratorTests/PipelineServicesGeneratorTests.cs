using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// The generated IMediator hands its own provider, the request's scope, to the pipeline
// behaviors. Static Mediator.Send has no provider to hand over.
public class PipelineServicesGeneratorTests
{
    private const string Assignment = "global::ZeroAlloc.Mediator.PipelineBehaviorServices.Current = _services;";

    private const string Handler = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        public readonly record struct Ping(string Message) : IRequest<string>;

        public class PingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken ct)
                => ValueTask.FromResult("Pong");
        }
        """;

    private const string Behavior = """

        [PipelineBehavior(Order = 0)]
        public class LoggingBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        }
        """;

    [Fact]
    public void MediatorService_Send_HandsItsProviderToThePipeline()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(Handler + Behavior);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var serviceStart = output.IndexOf("partial class MediatorService", StringComparison.Ordinal);
        Assert.True(serviceStart >= 0, "MediatorService was not emitted");

        // Exactly once, in MediatorService.Send, before the pipeline is entered.
        var first = output.IndexOf(Assignment, StringComparison.Ordinal);
        Assert.True(first > serviceStart, "the provider is not handed over in MediatorService.Send");
        Assert.Equal(-1, output.IndexOf(Assignment, first + Assignment.Length, StringComparison.Ordinal));
        Assert.True(first < output.IndexOf("LoggingBehavior.Handle", serviceStart, StringComparison.Ordinal));
    }

    [Fact]
    public void Send_WithoutBehaviors_DoesNotTouchThePipelineProvider()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler);

        Assert.DoesNotContain("PipelineBehaviorServices", output, StringComparison.Ordinal);
    }
}
