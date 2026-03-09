using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void ZM002_DuplicateHandler_EmitsError()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler1 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong1");
            }

            public class PingHandler2 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong2");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm002 = diagnostics.FirstOrDefault(d => d.Id == "ZM002");
        Assert.NotNull(zm002);
        Assert.Equal(DiagnosticSeverity.Error, zm002.Severity);
    }

    [Fact]
    public void ZM001_NoHandler_EmitsError()
    {
        var source = """
            using ZMediator;

            namespace TestApp;

            public readonly record struct Orphan(string Data) : IRequest<string>;
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm001 = diagnostics.FirstOrDefault(d => d.Id == "ZM001");
        Assert.NotNull(zm001);
        Assert.Equal(DiagnosticSeverity.Error, zm001.Severity);
    }

    [Fact]
    public void ZM001_NotEmitted_WhenHandlerExists()
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

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm001 = diagnostics.FirstOrDefault(d => d.Id == "ZM001");
        Assert.Null(zm001);
    }

    [Fact]
    public void ZM003_ClassRequest_EmitsWarning()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public class Ping : IRequest<string> { }

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm003 = diagnostics.FirstOrDefault(d => d.Id == "ZM003");
        Assert.NotNull(zm003);
        Assert.Equal(DiagnosticSeverity.Warning, zm003.Severity);
    }

    [Fact]
    public void ZM005_MissingHandleMethod_EmitsError()
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

            [PipelineBehavior(Order = 0)]
            public class BadBehavior : IPipelineBehavior
            {
                // Missing static Handle method!
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm005 = diagnostics.FirstOrDefault(d => d.Id == "ZM005");
        Assert.NotNull(zm005);
        Assert.Equal(DiagnosticSeverity.Error, zm005.Severity);
    }

    [Fact]
    public void ZM006_DuplicateOrder_EmitsWarning()
    {
        var source = """
            using ZMediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorA : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorB : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zm006 = diagnostics.FirstOrDefault(d => d.Id == "ZM006");
        Assert.NotNull(zm006);
        Assert.Equal(DiagnosticSeverity.Warning, zm006.Severity);
    }
}
