using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// Regression tests for ZeroAlloc.Saga#127.
///
/// Generated Publish used to be driven entirely by handlers discovered in the current
/// compilation's source, and dispatched to them by concrete type. Neither half reaches a handler
/// that another source generator emitted (Roslyn generators cannot observe each other's output)
/// or that was registered as <c>INotificationHandler&lt;T&gt;</c> rather than by concrete type —
/// which is exactly how ZeroAlloc.Saga registers its generated handlers.
/// </summary>
public class InterfaceRegisteredNotificationTests
{
    [Fact]
    public void Generator_EmitsPublish_ForNotificationWithNoSourceHandler()
    {
        // Models the saga case: the notification is declared in source, but its only handler is
        // emitted by another generator and is therefore invisible here. Publish must still be
        // emitted, or the call site does not compile at all.
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct OrderPlaced(int OrderId) : INotification;
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ValueTask Publish(global::TestApp.OrderPlaced notification", output);
    }

    [Fact]
    public void Generator_EmitsInterfaceEnumeration_ForNotificationWithNoSourceHandler()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct OrderPlaced(int OrderId) : INotification;
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(
            "GetServices<global::ZeroAlloc.Mediator.INotificationHandler<global::TestApp.OrderPlaced>>",
            output);
    }

    [Fact]
    public void Generator_EmitsInterfaceEnumeration_AlongsideCompileTimeHandlers()
    {
        // A notification that DOES have a source handler must still enumerate DI, otherwise a
        // host that declares its own handler silently shadows the saga's.
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class HostHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("global::TestApp.HostHandler", output);
        Assert.Contains(
            "GetServices<global::ZeroAlloc.Mediator.INotificationHandler<global::TestApp.OrderPlaced>>",
            output);
    }

    [Fact]
    public void Generator_EmitsDedupGuard_SoDoubleRegisteredHandlerRunsOnce()
    {
        // A handler registered by BOTH concrete type and interface must not be invoked twice.
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class HostHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("is global::TestApp.HostHandler", output);
    }
}
