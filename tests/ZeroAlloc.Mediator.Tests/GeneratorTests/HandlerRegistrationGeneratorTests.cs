using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

public class HandlerRegistrationGeneratorTests
{
    private const string Header = """
        using ZeroAlloc.Mediator;
        using Microsoft.Extensions.DependencyInjection;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        """;

    private static string Registration(string handler, string lifetime) =>
        $"services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({handler}), typeof({handler}), {lifetime}));";

    private static string Run(string body)
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(Header + body);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    [Fact]
    public void AddMediator_RegistersEachHandlerKind_WithTheDefaultLifetime()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }

            public readonly record struct Happened(int X) : INotification;
            public class HappenedHandler : INotificationHandler<Happened>
            {
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }

            public readonly record struct Count(int To) : IStreamRequest<int>;
            public class CountHandler : IStreamRequestHandler<Count, int>
            {
                public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }
            """);

        Assert.Contains(Registration("global::TestApp.PingHandler", "defaultHandlerLifetime"), output);
        Assert.Contains(Registration("global::TestApp.HappenedHandler", "defaultHandlerLifetime"), output);
        Assert.Contains(Registration("global::TestApp.CountHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_ParameterlessOverload_ForwardsWithTransient()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains("AddMediator(services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)", output);
        Assert.Contains("global::Microsoft.Extensions.DependencyInjection.ServiceLifetime defaultHandlerLifetime)", output);
    }

    [Theory]
    [InlineData("Singleton")]
    [InlineData("Scoped")]
    [InlineData("Transient")]
    public void AddMediator_UsesHandlerLifetimeAttribute_OverTheDefault(string lifetime)
    {
        var output = Run($$"""
            public readonly record struct Ping(int X) : IRequest<int>;
            [HandlerLifetime(ServiceLifetime.{{lifetime}})]
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            Registration("global::TestApp.PingHandler", $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime}"),
            output);
    }

    [Fact]
    public void AddMediator_RegistersInternalHandlers()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            internal class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(Registration("global::TestApp.PingHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_RegistersAClassHandlingSeveralMessagesOnce_InOrdinalOrder()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public readonly record struct Happened(int X) : INotification;
            public class Zeta : IRequestHandler<Ping, int>, INotificationHandler<Happened>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }

            public readonly record struct Other(int X) : IRequest<int>;
            public class Alpha : IRequestHandler<Other, int>
            {
                public ValueTask<int> Handle(Other request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        var zeta = Registration("global::TestApp.Zeta", "defaultHandlerLifetime");
        var alpha = Registration("global::TestApp.Alpha", "defaultHandlerLifetime");
        Assert.Equal(output.IndexOf(zeta, StringComparison.Ordinal), output.LastIndexOf(zeta, StringComparison.Ordinal));
        Assert.True(output.IndexOf(alpha, StringComparison.Ordinal) < output.IndexOf(zeta, StringComparison.Ordinal));
    }

    [Fact]
    public void AddMediator_WithoutHandlers_RegistersOnlyIMediator()
    {
        var output = Run("""
            public readonly record struct Happened(int X) : INotification;
            """);

        Assert.Contains("services.TryAddTransient<global::ZeroAlloc.Mediator.IMediator, global::ZeroAlloc.Mediator.MediatorService>();", output);
        Assert.DoesNotContain("services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor", output);
    }
}
