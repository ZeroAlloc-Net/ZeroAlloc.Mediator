using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

public class DiInterfaceGeneratorTests
{
    [Fact]
    public void Generator_EmitsIMediatorInterface_WithSendMethod()
    {
        var source = """
            using ZeroAlloc.Mediator;
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
        Assert.Contains("public partial interface IMediator", output);
        Assert.Contains("ValueTask<string> Send(global::TestApp.Ping request", output);
    }

    [Fact]
    public void Generator_EmitsIMediatorInterface_WithPublishMethod()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct UserCreated(int Id) : INotification;

            public class UserCreatedHandler : INotificationHandler<UserCreated>
            {
                public ValueTask Handle(UserCreated notification, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public partial interface IMediator", output);
        Assert.Contains("ValueTask Publish(global::TestApp.UserCreated notification", output);
    }

    [Fact]
    public void Generator_EmitsIMediatorInterface_WithCreateStreamMethod()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct CountTo(int Max) : IStreamRequest<int>;

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
        Assert.Contains("public partial interface IMediator", output);
        Assert.Contains("IAsyncEnumerable<int> CreateStream(global::TestApp.CountTo request", output);
    }

    [Fact]
    public void Generator_EmitsMediatorService_ResolvesHandlerFromInjectedProvider()
    {
        var source = """
            using ZeroAlloc.Mediator;
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
        Assert.Contains("public partial class MediatorService : IMediator", output);
        Assert.Contains("GetRequiredService<global::TestApp.PingHandler>(_services)", output);
    }

    [Fact]
    public void Generator_IMediator_ExcludesBaseNotificationHandlerPublish()
    {
        var source = """
            using ZeroAlloc.Mediator;
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

        // Interface should have Publish for concrete type
        Assert.Contains("ValueTask Publish(global::TestApp.UserCreated notification", output);

        // But NOT for INotification (base handler type)
        Assert.DoesNotContain("Publish(global::ZeroAlloc.Mediator.INotification notification", output);
    }

    [Fact]
    public void Generator_IMediator_HasAllMethodTypes()
    {
        var source = """
            using ZeroAlloc.Mediator;
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

        // Interface has all three method types
        var interfaceIdx = output.IndexOf("public partial interface IMediator", StringComparison.Ordinal);
        var serviceIdx = output.IndexOf("public partial class MediatorService", StringComparison.Ordinal);
        var interfaceSection = output.Substring(interfaceIdx, serviceIdx - interfaceIdx);

        Assert.Contains("Send(global::TestApp.Ping", interfaceSection);
        Assert.Contains("Publish(global::TestApp.UserCreated", interfaceSection);
        Assert.Contains("CreateStream(global::TestApp.CountTo", interfaceSection);

        // Service: Send resolves from DI; Publish + CreateStream still delegate to static dispatcher (Tasks 5-6).
        var serviceSection = output.Substring(serviceIdx);
        Assert.Contains("GetRequiredService<global::TestApp.PingHandler>(_services)", serviceSection);
        Assert.Contains("Mediator.Publish(notification, ct)", serviceSection);
        Assert.Contains("Mediator.CreateStream(request, ct)", serviceSection);
    }

    [Fact]
    public void Generator_MediatorService_Send_ResolvesHandlerFromInjectedProvider()
    {
        var source = """
            using ZeroAlloc.Mediator;
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

        Assert.Contains("public partial class MediatorService", output);
        Assert.Contains("private readonly global::System.IServiceProvider _services", output);
        Assert.Contains("public MediatorService(global::System.IServiceProvider services)", output);
        Assert.Contains(
            "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::TestApp.PingHandler>(_services)",
            output);
        // Old delegation must be gone for Send.
        Assert.DoesNotContain("=> Mediator.Send(request, ct);", output);
    }
}
