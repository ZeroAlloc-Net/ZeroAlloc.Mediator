using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class ConfigurationGeneratorTests
{
    [Fact]
    public void Generator_EmitsConfigureMethod()
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
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static void Configure(", output);
        Assert.Contains("MediatorConfig", output);
        Assert.Contains("SetFactory", output);
    }

    [Fact]
    public void Generator_MediatorConfig_HasIfElseChain_ForMultipleHandlers()
    {
        var source = """
            using ZMediator;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;
            public readonly record struct UserCreated(int Id) : INotification;
            public readonly record struct CountTo(int Max) : IStreamRequest<int>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class CountToHandler : IStreamRequestHandler<CountTo, int>
            {
                public async IAsyncEnumerable<int> Handle(
                    CountTo request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // All three handler types should be configurable
        Assert.Contains("Func<global::TestApp.PingHandler>", output);
        Assert.Contains("Func<global::TestApp.UserCreatedHandler>", output);
        Assert.Contains("Func<global::TestApp.CountToHandler>", output);

        // Should use if/else if chain
        Assert.Contains("if (factory is Func<global::TestApp.PingHandler>", output);
        Assert.Contains("else if", output);
    }

    [Fact]
    public void Generator_MediatorConfig_DeduplicatesNotificationHandlers()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct EventA : INotification;
            public readonly record struct EventB : INotification;

            // This handler handles both EventA and EventB — but it's two separate classes
            public class EventAHandler : INotificationHandler<EventA>
            {
                public ValueTask Handle(EventA notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class EventBHandler : INotificationHandler<EventB>
            {
                public ValueTask Handle(EventB notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Both handlers should appear in config
        Assert.Contains("Func<global::TestApp.EventAHandler>", output);
        Assert.Contains("Func<global::TestApp.EventBHandler>", output);
    }
}
