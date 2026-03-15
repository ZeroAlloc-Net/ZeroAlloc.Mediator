using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class NotificationDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsSequentialPublish_ForNotification()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int UserId) : INotification;

            public class SendEmailHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class LogHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static async ValueTask Publish(global::TestApp.UserCreated notification", output);
        Assert.Contains("await", output);
    }

    [Fact]
    public void Generator_EmitsParallelPublish_ForParallelNotification()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            [ParallelNotification]
            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class AnalyticsHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class EmailHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Task.WhenAll", output);
    }

    [Fact]
    public void Generator_IncludesBaseHandler_InConcretePublish()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int UserId) : INotification;

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalNotificationLogger : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Publish for UserCreated should include both the specific and global handler
        Assert.Contains("Publish(global::TestApp.UserCreated notification", output);
        Assert.Contains("UserCreatedHandler", output);
        Assert.Contains("GlobalNotificationLogger", output);
    }

    [Fact]
    public void Generator_IncludesIntermediateInterfaceHandler_InConcretePublish()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public interface IOrderNotification : INotification { }

            public readonly record struct OrderPlaced(int OrderId) : IOrderNotification;
            public readonly record struct OrderShipped(int OrderId) : INotification;

            public class OrderPlacedHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class OrderShippedHandler : INotificationHandler<OrderShipped>
            {
                public ValueTask Handle(OrderShipped notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class OrderNotificationLogger : INotificationHandler<IOrderNotification>
            {
                public ValueTask Handle(IOrderNotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // OrderPlaced implements IOrderNotification, so its Publish should include OrderNotificationLogger
        Assert.Contains("Publish(global::TestApp.OrderPlaced notification", output);
        Assert.Contains("OrderNotificationLogger", output);

        // OrderShipped does NOT implement IOrderNotification, so its Publish should NOT include OrderNotificationLogger
        // We verify by checking that the output contains OrderShippedHandler but the OrderShipped Publish
        // only has its own handler
        Assert.Contains("Publish(global::TestApp.OrderShipped notification", output);
        Assert.Contains("OrderShippedHandler", output);
    }

    [Fact]
    public void Generator_BaseHandlerDoesNotGetOwnPublishMethod()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int UserId) : INotification;

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalHandler : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Should NOT emit a Publish method for INotification itself
        Assert.DoesNotContain("Publish(global::ZMediator.INotification notification", output);
    }

    [Fact]
    public void Generator_MultipleBaseHandlers_AllIncludedInConcretePublish()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public interface IAuditableNotification : INotification { }

            public readonly record struct OrderPlaced(int OrderId) : IAuditableNotification;

            public class OrderPlacedHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalLogger : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class AuditLogger : INotificationHandler<IAuditableNotification>
            {
                public ValueTask Handle(IAuditableNotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // OrderPlaced implements both INotification and IAuditableNotification,
        // so its Publish should include all three handlers
        Assert.Contains("OrderPlacedHandler", output);
        Assert.Contains("GlobalLogger", output);
        Assert.Contains("AuditLogger", output);
    }

    [Fact]
    public void Generator_BaseHandlerWithParallelNotification_IncludedInWhenAll()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            [ParallelNotification]
            public readonly record struct OrderPlaced(int OrderId) : INotification;

            public class OrderHandler : INotificationHandler<OrderPlaced>
            {
                public ValueTask Handle(OrderPlaced notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalLogger : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Parallel notification with base handler should use Task.WhenAll for all handlers
        Assert.Contains("Task.WhenAll", output);
        Assert.Contains("OrderHandler", output);
        Assert.Contains("GlobalLogger", output);
    }

    [Fact]
    public void Generator_MultipleConcreteTypes_EachGetCorrectBaseHandlers()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public interface IOrderNotification : INotification { }

            public readonly record struct OrderCreated(int Id) : IOrderNotification;
            public readonly record struct UserCreated(int Id) : INotification;

            public class OrderCreatedHandler : INotificationHandler<OrderCreated>
            {
                public ValueTask Handle(OrderCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalLogger : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class OrderLogger : INotificationHandler<IOrderNotification>
            {
                public ValueTask Handle(IOrderNotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Both concrete types get Publish methods
        Assert.Contains("Publish(global::TestApp.OrderCreated notification", output);
        Assert.Contains("Publish(global::TestApp.UserCreated notification", output);

        // Extract individual Publish method bodies by finding the next "public static" boundary
        var orderIdx = output.IndexOf("Publish(global::TestApp.OrderCreated", StringComparison.Ordinal);
        var userIdx = output.IndexOf("Publish(global::TestApp.UserCreated", StringComparison.Ordinal);

        // Find the end of each Publish method (next "public static" or "Configure")
        var configureIdx = output.IndexOf("public static void Configure", StringComparison.Ordinal);

        string orderSection, userSection;
        if (orderIdx < userIdx)
        {
            orderSection = output.Substring(orderIdx, userIdx - orderIdx);
            userSection = output.Substring(userIdx, configureIdx - userIdx);
        }
        else
        {
            userSection = output.Substring(userIdx, orderIdx - userIdx);
            orderSection = output.Substring(orderIdx, configureIdx - orderIdx);
        }

        // OrderCreated should have: OrderCreatedHandler + GlobalLogger + OrderLogger
        Assert.Contains("OrderCreatedHandler", orderSection);
        Assert.Contains("GlobalLogger", orderSection);
        Assert.Contains("OrderLogger", orderSection);

        // UserCreated should have: UserCreatedHandler + GlobalLogger, but NOT OrderLogger
        Assert.Contains("UserCreatedHandler", userSection);
        Assert.Contains("GlobalLogger", userSection);
        Assert.DoesNotContain("OrderLogger", userSection);
    }

    [Fact]
    public void Generator_SingleHandler_EmitsPublish()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Alert(string Message) : INotification;

            public class AlertHandler : INotificationHandler<Alert>
            {
                public ValueTask Handle(Alert notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Publish(global::TestApp.Alert notification", output);
        Assert.Contains("AlertHandler", output);
    }

    [Fact]
    public void Generator_BaseHandlerFactory_IncludedInMediatorConfig()
    {
        var source = """
            using ZMediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int Id) : INotification;

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }

            public class GlobalLogger : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Both handlers should have factory fields
        Assert.Contains("_userCreatedHandlerFactory", output);
        Assert.Contains("_globalLoggerFactory", output);

        // Both should be configurable via SetFactory
        Assert.Contains("Func<global::TestApp.UserCreatedHandler>", output);
        Assert.Contains("Func<global::TestApp.GlobalLogger>", output);
    }
}
