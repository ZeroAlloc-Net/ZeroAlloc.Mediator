using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// The generated IMediator hands its own provider, the request's scope, to the pipeline
// behaviors: as a Handle argument to a behavior that takes one, and through
// PipelineBehaviorServices.Current to one that may read it. Setting Current is an AsyncLocal
// write that allocates per Send, so it is emitted only when such a behavior is in the pipeline.
// Static Mediator.Send has no provider to hand over.
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

    private const string OptedOutBehavior = """

        [PipelineBehavior(Order = 1, UsesPipelineBehaviorServices = false)]
        public class TimingBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        }
        """;

    private const string ServicesBehavior = """

        [PipelineBehavior(Order = 2)]
        public class ScopedBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);

            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, IServiceProvider services, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        }
        """;

    [Fact]
    public void Send_WhenNoBehaviorReadsCurrent_DoesNotSetIt()
    {
        var (output, generatorDiagnostics, compilationDiagnostics) =
            GeneratorTestHelper.RunGeneratorAndCompile(Handler + OptedOutBehavior + ServicesBehavior);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.DoesNotContain(Assignment, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_PassesItsProviderToABehaviorThatTakesOne()
    {
        var (output, _, compilationDiagnostics) =
            GeneratorTestHelper.RunGeneratorAndCompile(Handler + OptedOutBehavior + ServicesBehavior);

        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var serviceStart = output.IndexOf("partial class MediatorService", StringComparison.Ordinal);
        var call = output.IndexOf("ScopedBehavior.Handle", serviceStart, StringComparison.Ordinal);
        Assert.True(call > serviceStart, "ScopedBehavior is not in MediatorService.Send");
        // TimingBehavior, Order 1, wraps it, so it is called with level-1 arguments.
        var callEnd = output.IndexOf("(r2, c2)", call, StringComparison.Ordinal);
        Assert.Contains("r1, _services, c1,", output.Substring(call, callEnd - call), StringComparison.Ordinal);

        // The static Mediator class has no provider and calls the overload without one.
        var staticCall = output.IndexOf("ScopedBehavior.Handle", StringComparison.Ordinal);
        Assert.True(staticCall < serviceStart);
        Assert.DoesNotContain("_services", output.Substring(staticCall, serviceStart - staticCall), StringComparison.Ordinal);
    }

    [Fact]
    public void Send_SetsCurrent_WhenOneBehaviorMayReadIt()
    {
        // LoggingBehavior neither takes the provider nor opts out, so it may read Current.
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler + Behavior + OptedOutBehavior + ServicesBehavior);

        Assert.Contains(Assignment, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_WithoutBehaviors_DoesNotTouchThePipelineProvider()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler);

        Assert.DoesNotContain("PipelineBehaviorServices", output, StringComparison.Ordinal);
    }
}
