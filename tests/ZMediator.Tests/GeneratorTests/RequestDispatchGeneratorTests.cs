using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class RequestDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsSendMethod_ForRequestHandler()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult($"Pong: {request.Message}");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static ValueTask<string> Send(global::TestApp.Ping request", output);
    }

    [Fact]
    public void Generator_EmitsSendMethod_ForVoidRequest()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct DoSomething : IRequest;

            public class DoSomethingHandler : IRequestHandler<DoSomething, Unit>
            {
                public ValueTask<Unit> Handle(DoSomething request, CancellationToken ct)
                    => ValueTask.FromResult(Unit.Value);
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static ValueTask<global::ZMediator.Unit> Send(global::TestApp.DoSomething request", output);
    }
}
