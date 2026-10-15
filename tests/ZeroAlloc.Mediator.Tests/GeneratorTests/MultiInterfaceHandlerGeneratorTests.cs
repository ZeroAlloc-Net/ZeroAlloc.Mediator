using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// A class that implements a handler interface more than once is dispatched for every one of
// them, through one factory field, one cached instance and one DI registration (#334). Before,
// only the first interface was seen and the rest were dropped without a diagnostic.
public class MultiInterfaceHandlerGeneratorTests
{
    private const string Header = """
        using ZeroAlloc.Mediator;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        """;

    private static string Run(string body)
    {
        var (output, generatorDiagnostics, compilationDiagnostics) = GeneratorTestHelper.RunGeneratorAndCompile(Header + body);
        Assert.Empty(generatorDiagnostics);
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    private static int Count(string output, string text) =>
        output.Split(text, StringSplitOptions.None).Length - 1;

    private const string Registration =
        "services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::TestApp.AuditHandler), typeof(global::TestApp.AuditHandler), defaultHandlerLifetime));";

    [Fact]
    public void ClassHandlingTwoNotifications_IsDispatchedForBoth()
    {
        var output = Run("""
            public readonly record struct OrderPlaced(int Id) : INotification;
            public readonly record struct OrderShipped(int Id) : INotification;

            public sealed class AuditHandler : INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct) => default;
                public ValueTask Handle(OrderShipped notification, CancellationToken ct) => default;
            }
            """);

        // Static Mediator: a Publish per notification, each calling the shared handler.
        Assert.Contains("public static async ValueTask Publish(global::TestApp.OrderPlaced notification, CancellationToken ct = default)", output);
        Assert.Contains("public static async ValueTask Publish(global::TestApp.OrderShipped notification, CancellationToken ct = default)", output);
        Assert.Equal(2, Count(output, "await (_auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance())).Handle(notification, ct);"));

        // MediatorService: both Publish methods resolve the one concrete registration.
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask Publish(global::TestApp.OrderShipped notification, global::System.Threading.CancellationToken ct)", output);
        Assert.Equal(2, Count(output, "GetService<global::TestApp.AuditHandler>(_services);"));

        AssertSharedOnce(output);
    }

    [Fact]
    public void ClassHandlingTwoRequests_IsDispatchedForBoth()
    {
        var output = Run("""
            public readonly record struct GetName(int Id) : IRequest<string>;
            public readonly record struct GetAge(int Id) : IRequest<int>;

            public sealed class AuditHandler : IRequestHandler<GetName, string>, IRequestHandler<GetAge, int>
            {
                public ValueTask<string> Handle(GetName request, CancellationToken ct) => new("n");
                public ValueTask<int> Handle(GetAge request, CancellationToken ct) => new(1);
            }
            """);

        Assert.Contains("public static async ValueTask<string> Send(global::TestApp.GetName request, CancellationToken ct = default)", output);
        Assert.Contains("public static async ValueTask<int> Send(global::TestApp.GetAge request, CancellationToken ct = default)", output);
        Assert.Equal(2, Count(output, "var handler = _auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance());"));

        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<string> Send(global::TestApp.GetName request, global::System.Threading.CancellationToken ct)", output);
        Assert.Contains("public async global::System.Threading.Tasks.ValueTask<int> Send(global::TestApp.GetAge request, global::System.Threading.CancellationToken ct)", output);
        Assert.Equal(2, Count(output, "GetRequiredService<global::TestApp.AuditHandler>(_services);"));

        AssertSharedOnce(output);
    }

    [Fact]
    public void ClassHandlingTwoStreams_IsDispatchedForBoth()
    {
        var output = Run("""
            public readonly record struct Numbers(int To) : IStreamRequest<int>;
            public readonly record struct Words(int To) : IStreamRequest<string>;

            public sealed class AuditHandler : IStreamRequestHandler<Numbers, int>, IStreamRequestHandler<Words, string>
            {
                public async IAsyncEnumerable<int> Handle(Numbers request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }

                public async IAsyncEnumerable<string> Handle(Words request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return "a";
                    await Task.CompletedTask;
                }
            }
            """);

        Assert.Contains("CreateStream(global::TestApp.Numbers request", output);
        Assert.Contains("CreateStream(global::TestApp.Words request", output);
        AssertSharedOnce(output);
    }

    [Fact]
    public void ClassHandlingRequestsAndNotifications_IsDispatchedForEvery_Interface()
    {
        var output = Run("""
            public readonly record struct GetName(int Id) : IRequest<string>;
            public readonly record struct GetAge(int Id) : IRequest<int>;
            public readonly record struct OrderPlaced(int Id) : INotification;
            public readonly record struct OrderShipped(int Id) : INotification;

            public sealed class AuditHandler :
                IRequestHandler<GetName, string>, IRequestHandler<GetAge, int>,
                INotificationHandler<OrderPlaced>, INotificationHandler<OrderShipped>
            {
                public ValueTask<string> Handle(GetName request, CancellationToken ct) => new("n");
                public ValueTask<int> Handle(GetAge request, CancellationToken ct) => new(1);
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct) => default;
                public ValueTask Handle(OrderShipped notification, CancellationToken ct) => default;
            }
            """);

        Assert.Equal(2, Count(output, "var handler = _auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance());"));
        Assert.Equal(2, Count(output, "await (_auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance())).Handle(notification, ct);"));
        AssertSharedOnce(output);
    }

    // A partial class is described by its first declaration with a base list only. Each
    // declaration used to describe the class again, so a second base list made ZAM002 report the
    // class as a duplicate of itself.
    [Fact]
    public void PartialClassWithTwoBaseLists_IsDescribedOnce()
    {
        var output = Run("""
            public readonly record struct GetName(int Id) : IRequest<string>;
            public readonly record struct OrderPlaced(int Id) : INotification;

            public sealed partial class AuditHandler : IRequestHandler<GetName, string>
            {
                public ValueTask<string> Handle(GetName request, CancellationToken ct) => new("n");
            }

            public sealed partial class AuditHandler : INotificationHandler<OrderPlaced>, System.IDisposable
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct) => default;
                public void Dispose() { }
            }
            """);

        Assert.Equal(1, Count(output, "var handler = _auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance());"));
        Assert.Equal(1, Count(output, "await (_auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance())).Handle(notification, ct);"));
        AssertSharedOnce(output);
    }

    // A class handling a notification and a base interface of it gets the notification once per
    // interface, and each call binds to its own overload: the base one through a conversion.
    [Fact]
    public void ClassHandlingANotificationAndItsBase_CallsEachOverload()
    {
        var output = Run("""
            public interface IOrderEvent : INotification { }
            public readonly record struct OrderPlaced(int Id) : IOrderEvent;

            public sealed class AuditHandler : INotificationHandler<OrderPlaced>, INotificationHandler<IOrderEvent>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct) => default;
                public ValueTask Handle(IOrderEvent notification, CancellationToken ct) => default;
            }
            """);

        Assert.Contains("await (_auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance())).Handle(notification, ct);", output);
        Assert.Contains("await (_auditHandlerFactory?.Invoke() ?? (_auditHandlerInstance ?? __CreateAuditHandlerInstance())).Handle((global::TestApp.IOrderEvent)notification, ct);", output);
        Assert.Contains("await __h1.Handle((global::TestApp.IOrderEvent)notification, ct).ConfigureAwait(false);", output);
        AssertSharedOnce(output);
    }

    // One factory field, one instance cache, one SetFactory branch and one DI registration for the
    // class, however many interfaces it implements.
    private static void AssertSharedOnce(string output)
    {
        Assert.Equal(1, Count(output, "internal static Func<global::TestApp.AuditHandler>? _auditHandlerFactory;"));
        Assert.Equal(1, Count(output, "private static global::TestApp.AuditHandler? _auditHandlerInstance;"));
        Assert.Equal(1, Count(output, "private static global::TestApp.AuditHandler __CreateAuditHandlerInstance()"));
        Assert.Equal(1, Count(output, "(factory is Func<global::TestApp.AuditHandler>"));
        Assert.Equal(1, Count(output, Registration));
    }
}
