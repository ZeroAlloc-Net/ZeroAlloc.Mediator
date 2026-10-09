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
        var (output, generatorDiagnostics, compilationDiagnostics) = GeneratorTestHelper.RunGeneratorAndCompile(Header + body);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
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

    [Fact]
    public void AddMediator_DoesNotRegisterAbstractHandlers()
    {
        var output = Run("""
            public readonly record struct Happened(int X) : INotification;
            public abstract class BaseHandler : INotificationHandler<Happened>
            {
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }
            public class ConcreteHandler : BaseHandler;
            """);

        Assert.DoesNotContain("typeof(global::TestApp.BaseHandler)", output);
        Assert.Contains(Registration("global::TestApp.ConcreteHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_SkipsOpenGenericStreamHandlers()
    {
        var output = Run("""
            public readonly record struct Count(int To) : IStreamRequest<int>;
            public class GenericCountHandler<T> : IStreamRequestHandler<Count, int>
            {
                public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }
            """);

        Assert.DoesNotContain("GenericCountHandler", output);
    }

    [Fact]
    public void AddMediator_OutOfRangeHandlerLifetime_IsNotReplacedByTheDefault()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            [HandlerLifetime((ServiceLifetime)7)]
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            Registration("global::TestApp.PingHandler", "(global::Microsoft.Extensions.DependencyInjection.ServiceLifetime)7"),
            output);
    }

    [Fact]
    public void AddMediator_DoesNotRegisterHandlersWithoutAPublicConstructor()
    {
        // Microsoft DI builds only through a public constructor, so registering these would make
        // ValidateOnBuild fail even when the app supplies its own factory for them.
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            internal sealed class PingHandler : IRequestHandler<Ping, int>
            {
                internal PingHandler() { }
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }

            public readonly record struct Happened(int X) : INotification;
            public class HappenedHandler : INotificationHandler<Happened>
            {
                private HappenedHandler() { }
                public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
            }

            public readonly record struct Count(int To) : IStreamRequest<int>;
            public class CountHandler : IStreamRequestHandler<Count, int>
            {
                protected CountHandler() { }
                public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await Task.CompletedTask;
                }
            }

            public readonly record struct Other(int X) : IRequest<int>;
            public class OtherHandler : IRequestHandler<Other, int>
            {
                private OtherHandler() { }
                public OtherHandler(string dependency) { }
                public ValueTask<int> Handle(Other request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.DoesNotContain("typeof(global::TestApp.PingHandler)", output);
        Assert.DoesNotContain("typeof(global::TestApp.HappenedHandler)", output);
        Assert.DoesNotContain("typeof(global::TestApp.CountHandler)", output);
        Assert.Contains(Registration("global::TestApp.OtherHandler", "defaultHandlerLifetime"), output);
    }

    [Fact]
    public void AddMediator_SkipsHandlersNestedInAGenericType()
    {
        var output = Run("""
            public readonly record struct Ping(int X) : IRequest<int>;
            public readonly record struct Happened(int X) : INotification;
            public readonly record struct Count(int To) : IStreamRequest<int>;

            // Ping still needs a handler the generated code can name.
            public class TopLevelPingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }

            public class Outer<T>
            {
                public class PingHandler : IRequestHandler<Ping, int>
                {
                    public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
                }

                public class HappenedHandler : INotificationHandler<Happened>
                {
                    public ValueTask Handle(Happened notification, CancellationToken ct) => ValueTask.CompletedTask;
                }

                public class CountHandler : IStreamRequestHandler<Count, int>
                {
                    public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
                    {
                        yield return 1;
                        await Task.CompletedTask;
                    }
                }
            }
            """);

        Assert.DoesNotContain("Outer", output);
        Assert.Contains(Registration("global::TestApp.TopLevelPingHandler", "defaultHandlerLifetime"), output);
    }

    // Stand-ins for ZeroAlloc.Inject's lifetime attributes. The generator matches them by name,
    // so Mediator does not reference the package.
    private const string InjectAttributes = """
        namespace ZeroAlloc.Inject
        {
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class TransientAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class ScopedAttribute : System.Attribute { }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class SingletonAttribute : System.Attribute { }
        }

        """;

    private static string RunWithInjectAttributes(string body)
    {
        var source = Header.Replace("namespace TestApp;", InjectAttributes + "namespace TestApp\n{\n", StringComparison.Ordinal)
            + body + "\n}\n";
        var (output, generatorDiagnostics, compilationDiagnostics) = GeneratorTestHelper.RunGeneratorAndCompile(source);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    [Theory]
    [InlineData("Transient", "Transient")]
    [InlineData("Scoped", "Scoped")]
    [InlineData("Singleton", "Singleton")]
    public void AddMediator_UsesInjectLifetimeAttribute_OverTheDefault(string injectAttribute, string lifetime)
    {
        var output = RunWithInjectAttributes($$"""
            public readonly record struct Ping(int X) : IRequest<int>;
            [ZeroAlloc.Inject.{{injectAttribute}}]
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            Registration("global::TestApp.PingHandler", $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime}"),
            output);
    }

    [Theory]
    [InlineData("[HandlerLifetime(ServiceLifetime.Singleton)]\n[ZeroAlloc.Inject.Scoped]")]
    [InlineData("[ZeroAlloc.Inject.Scoped]\n[HandlerLifetime(ServiceLifetime.Singleton)]")]
    public void AddMediator_HandlerLifetimeAttribute_WinsOverAnInjectAttribute(string attributes)
    {
        var output = RunWithInjectAttributes($$"""
            public readonly record struct Ping(int X) : IRequest<int>;
            {{attributes}}
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(
            Registration("global::TestApp.PingHandler", "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton"),
            output);
    }

    [Fact]
    public void AddMediator_IgnoresSameNamedAttributesOutsideTheInjectNamespace()
    {
        var output = RunWithInjectAttributes("""
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class ScopedAttribute : System.Attribute { }

            public readonly record struct Ping(int X) : IRequest<int>;
            [Scoped]
            public class PingHandler : IRequestHandler<Ping, int>
            {
                public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(1);
            }
            """);

        Assert.Contains(Registration("global::TestApp.PingHandler", "defaultHandlerLifetime"), output);
    }
}
