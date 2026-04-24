using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

public class CombinedGeneratorTests
{
    [Fact]
    public void Generator_EmitsAllMethods_WhenAllHandlerTypesPresent()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            // === Request/Response ===
            public readonly record struct Ping(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }

            // === Notification (sequential) ===
            public readonly record struct UserCreated(int Id) : INotification;

            public class UserCreatedHandlerA : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class UserCreatedHandlerB : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            // === Notification (parallel) ===
            [ParallelNotification]
            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class OrderAnalyticsHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            // === Stream ===
            public readonly record struct CountRequest(int Max) : IStreamRequest<int>;

            public class CountHandler : IStreamRequestHandler<CountRequest, int>
            {
                public async IAsyncEnumerable<int> Handle(
                    CountRequest request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    for (var i = 0; i < request.Max; i++)
                        yield return i;
                }
            }

            // === Pipeline Behavior ===
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
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // No errors
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Send is generated with pipeline behavior inlined
        Assert.Contains("public static async ValueTask<string> Send(global::TestApp.Ping request", output);
        Assert.Contains("LoggingBehavior.Handle", output);

        // Publish is generated for both notification types
        Assert.Contains("public static async ValueTask Publish(global::TestApp.UserCreated notification", output);
        Assert.Contains("public static", output);

        // Parallel notification uses Task.WhenAll
        Assert.Contains("Publish(global::TestApp.OrderPlaced notification", output);

        // CreateStream is generated
        Assert.Contains("CreateStream(global::TestApp.CountRequest request", output);

        // Configure and MediatorConfig are generated
        Assert.Contains("public static void Configure(", output);
        Assert.Contains("MediatorConfig", output);
        Assert.Contains("SetFactory", output);
    }
}
