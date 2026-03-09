using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class PipelineBehaviorGeneratorTests
{
    [Fact]
    public void Generator_InlinesPipelineBehaviors_InOrder()
    {
        var source = """
            using ZMediator;
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

            [PipelineBehavior(Order = 0)]
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }

            [PipelineBehavior(Order = 1)]
            public class ValidationBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("LoggingBehavior", output);
        Assert.Contains("ValidationBehavior", output);
        var loggingIdx = output.IndexOf("LoggingBehavior.Handle", StringComparison.Ordinal);
        var validationIdx = output.IndexOf("ValidationBehavior.Handle", StringComparison.Ordinal);
        Assert.True(loggingIdx < validationIdx, "LoggingBehavior should wrap ValidationBehavior");
    }

    [Fact]
    public void Generator_ScopedBehavior_OnlyAppliedToTargetRequest()
    {
        var source = """
            using ZMediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping(string Message) : IRequest<string>;
            public readonly record struct Pong(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            public class PongHandler : IRequestHandler<Pong, string>
            {
                public ValueTask<string> Handle(Pong request, CancellationToken ct)
                    => ValueTask.FromResult("Ping");
            }

            [PipelineBehavior(Order = 0, AppliesTo = typeof(Ping))]
            public class PingOnlyBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                {
                    return next(request, ct);
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var pingSendIdx = output.IndexOf("Send(global::TestApp.Ping", StringComparison.Ordinal);
        var pongSendIdx = output.IndexOf("Send(global::TestApp.Pong", StringComparison.Ordinal);
        var pingSection = output.Substring(pingSendIdx, pongSendIdx - pingSendIdx);
        var pongSection = output.Substring(pongSendIdx);

        Assert.Contains("PingOnlyBehavior", pingSection);
        Assert.DoesNotContain("PingOnlyBehavior", pongSection);
    }
}
