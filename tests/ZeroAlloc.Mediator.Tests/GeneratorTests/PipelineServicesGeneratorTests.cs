using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// The generated IMediator threads its own provider, the request's scope, through the pipeline as
// the state of every behavior's Handle, so each lambda of the chain is static and the compiler
// creates it once. The static Mediator has no provider: its state is null.
public class PipelineServicesGeneratorTests
{
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

    private const string OtherBehavior = """

        [PipelineBehavior(Order = 1)]
        public class TimingBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
                TRequest request, TState state, CancellationToken ct,
                Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, state, ct);
        }
        """;

    private const string Behavior = """

        [PipelineBehavior(Order = 0)]
        public class LoggingBehavior : IPipelineBehavior
        {
            public static ValueTask<TResponse> Handle<TRequest, TResponse, TState>(
                TRequest request, TState state, CancellationToken ct,
                Func<TRequest, TState, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, state, ct);
        }
        """;

    private static string MediatorServiceSource(string output)
    {
        var serviceStart = output.IndexOf("partial class MediatorService", StringComparison.Ordinal);
        Assert.True(serviceStart >= 0, "MediatorService was not emitted");
        return output.Substring(serviceStart);
    }

    [Fact]
    public void MediatorService_Send_ThreadsItsProviderThroughNext()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(Handler + Behavior);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var service = MediatorServiceSource(output);
        Assert.Contains(
            "LoggingBehavior.Handle<global::TestApp.Ping, string, global::System.IServiceProvider>(",
            service, StringComparison.Ordinal);
        Assert.Contains("request, _services, ct,", service, StringComparison.Ordinal);
        // The innermost body resolves the handler from the state, not from the captured field.
        Assert.Contains("GetRequiredService<global::TestApp.PingHandler>(s1)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void MediatorService_Send_EmitsOnlyStaticLambdas()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler + Behavior + OtherBehavior);

        var service = MediatorServiceSource(output);
        var lambdas = System.Text.RegularExpressions.Regex.Matches(
            service, @"(?:static )?\(r\d+, s\d+, c\d+\) =>", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEmpty(lambdas);
        Assert.All(lambdas.Cast<System.Text.RegularExpressions.Match>(), m => Assert.StartsWith("static ", m.Value, StringComparison.Ordinal));
    }

    [Fact]
    public void MediatorService_Send_DoesNotCaptureTheProviderField()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler + Behavior + OtherBehavior);

        // _services is read once, as the state argument of the outermost call.
        var service = MediatorServiceSource(output);
        var sendStart = service.IndexOf("Send(global::TestApp.Ping request", StringComparison.Ordinal);
        Assert.True(sendStart >= 0);
        var send = service.Substring(sendStart, service.IndexOf("catch", sendStart, StringComparison.Ordinal) - sendStart);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            send, "_services", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void StaticMediator_Send_PassesANullState()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler + Behavior);

        var staticStart = output.IndexOf("LoggingBehavior.Handle", StringComparison.Ordinal);
        var serviceStart = output.IndexOf("partial class MediatorService", StringComparison.Ordinal);
        Assert.True(staticStart >= 0 && staticStart < serviceStart);
        var call = output.Substring(staticStart, 160);
        Assert.StartsWith("LoggingBehavior.Handle<global::TestApp.Ping, string, global::System.IServiceProvider?>(", call, StringComparison.Ordinal);
        Assert.Contains("request, null, ct,", call, StringComparison.Ordinal);
    }

    [Fact]
    public void Send_NeverSetsAnAmbientProvider()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler + Behavior);

        Assert.DoesNotContain("PipelineBehaviorServices", output, StringComparison.Ordinal);
    }

    [Fact]
    public void BehaviorsAndGeneratedCode_Compile()
    {
        var (_, generatorDiagnostics, compilationDiagnostics) =
            GeneratorTestHelper.RunGeneratorAndCompile(Handler + Behavior + OtherBehavior);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Send_WithoutBehaviors_ResolvesTheHandlerFromTheField()
    {
        var (output, _) = GeneratorTestHelper.RunGenerator(Handler);

        Assert.DoesNotContain("PipelineBehaviorServices", output, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<global::TestApp.PingHandler>(_services)", output, StringComparison.Ordinal);
    }
}
