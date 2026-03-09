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

    [Fact]
    public void Generator_SingleBehavior_WrapsHandler()
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
            public class LoggingBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should have the behavior wrapping the handler with static lambda
        Assert.Contains("LoggingBehavior.Handle<global::TestApp.Ping, string>", output);
        Assert.Contains("static (r1, c1)", output);
    }

    [Fact]
    public void Generator_ThreeBehaviors_NestedInOrder()
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
            public class First : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 1)]
            public class Second : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 2)]
            public class Third : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Verify ordering: First wraps Second wraps Third wraps handler
        var firstIdx = output.IndexOf("First.Handle", StringComparison.Ordinal);
        var secondIdx = output.IndexOf("Second.Handle", StringComparison.Ordinal);
        var thirdIdx = output.IndexOf("Third.Handle", StringComparison.Ordinal);
        Assert.True(firstIdx < secondIdx, "First should come before Second");
        Assert.True(secondIdx < thirdIdx, "Second should come before Third");

        // Should have unique lambda params for each nesting level
        Assert.Contains("static (r3, c3)", output);
    }

    [Fact]
    public void Generator_GlobalAndScopedBehavior_BothApplied()
    {
        var source = """
            using ZMediator;
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct CreateUser(string Name) : IRequest<int>;

            public class CreateUserHandler : IRequestHandler<CreateUser, int>
            {
                public ValueTask<int> Handle(CreateUser request, CancellationToken ct)
                    => ValueTask.FromResult(1);
            }

            [PipelineBehavior(Order = 0)]
            public class GlobalLogging : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 1, AppliesTo = typeof(CreateUser))]
            public class CreateUserValidation : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Both behaviors should be present in the Send method
        Assert.Contains("GlobalLogging.Handle", output);
        Assert.Contains("CreateUserValidation.Handle", output);

        // Global should wrap scoped (order 0 before order 1)
        var globalIdx = output.IndexOf("GlobalLogging.Handle", StringComparison.Ordinal);
        var scopedIdx = output.IndexOf("CreateUserValidation.Handle", StringComparison.Ordinal);
        Assert.True(globalIdx < scopedIdx, "Global should wrap scoped");
    }
}
