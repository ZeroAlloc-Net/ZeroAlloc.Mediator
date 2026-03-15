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

    [Fact]
    public void Generator_EmitsMultipleSendMethods_ForMultipleRequestTypes()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;
            public readonly record struct GetUser(int Id) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            public class GetUserHandler : IRequestHandler<GetUser, string>
            {
                public ValueTask<string> Handle(GetUser request, CancellationToken ct)
                    => ValueTask.FromResult("User");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Send(global::TestApp.Ping request", output);
        Assert.Contains("Send(global::TestApp.GetUser request", output);
    }

    [Fact]
    public void Generator_SkipsPrivateHandler()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;

            private class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Private handler should be skipped — no Send method, ZM001 should fire
        Assert.DoesNotContain("Send(global::TestApp.Ping", output);
    }

    [Fact]
    public void Generator_SendWithoutPipeline_DirectHandlerCall()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Without pipeline, should call handler directly (no Behavior.Handle)
        Assert.Contains("handler.Handle(request, ct)", output);
        Assert.DoesNotContain("Behavior", output);
    }

    [Fact]
    public void Generator_EmitsFactoryField_ForHandler()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("internal static Func<global::TestApp.PingHandler>? _pingHandlerFactory", output);
    }
}
