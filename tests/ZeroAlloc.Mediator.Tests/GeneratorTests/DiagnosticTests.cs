using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Globalization;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void ZAM002_DuplicateHandler_EmitsError()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler1 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong1");
            }

            public class PingHandler2 : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong2");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam002 = diagnostics.FirstOrDefault(d => d.Id == "ZAM002");
        Assert.NotNull(zam002);
        Assert.Equal(DiagnosticSeverity.Error, zam002.Severity);
    }

    [Fact]
    public void ZAM001_NoHandler_EmitsError()
    {
        var source = """
            using ZeroAlloc.Mediator;

            namespace TestApp;

            public readonly record struct Orphan(string Data) : IRequest<string>;
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam001 = diagnostics.FirstOrDefault(d => d.Id == "ZAM001");
        Assert.NotNull(zam001);
        Assert.Equal(DiagnosticSeverity.Error, zam001.Severity);
    }

    [Fact]
    public void ZAM001_NotEmitted_WhenHandlerExists()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam001 = diagnostics.FirstOrDefault(d => d.Id == "ZAM001");
        Assert.Null(zam001);
    }

    [Fact]
    public void ZAM003_ClassRequest_EmitsWarning()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public class Ping : IRequest<string> { }

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam003 = diagnostics.FirstOrDefault(d => d.Id == "ZAM003");
        Assert.NotNull(zam003);
        Assert.Equal(DiagnosticSeverity.Warning, zam003.Severity);
    }

    [Fact]
    public void ZAM005_MissingHandleMethod_EmitsError()
    {
        var source = """
            using ZeroAlloc.Mediator;
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
            public class BadBehavior : IPipelineBehavior
            {
                // Missing static Handle method!
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam005 = diagnostics.FirstOrDefault(d => d.Id == "ZAM005");
        Assert.NotNull(zam005);
        Assert.Equal(DiagnosticSeverity.Error, zam005.Severity);
    }

    [Fact]
    public void ZAM006_DuplicateOrder_EmitsWarning()
    {
        var source = """
            using ZeroAlloc.Mediator;
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
            public class BehaviorA : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }

            [PipelineBehavior(Order = 0)]
            public class BehaviorB : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam006 = diagnostics.FirstOrDefault(d => d.Id == "ZAM006");
        Assert.NotNull(zam006);
        Assert.Equal(DiagnosticSeverity.Warning, zam006.Severity);
    }

    [Fact]
    public void ZAM003_NotEmitted_ForStructRequest()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("Pong");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam003 = diagnostics.FirstOrDefault(d => d.Id == "ZAM003");
        Assert.Null(zam003);
    }

    [Fact]
    public void ZAM005_NotEmitted_WhenValidHandleMethodExists()
    {
        var source = """
            using ZeroAlloc.Mediator;
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
            public class GoodBehavior : IPipelineBehavior
            {
                public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                    TRequest request, CancellationToken ct,
                    Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                    where TRequest : IRequest<TResponse>
                    => next(request, ct);
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam005 = diagnostics.FirstOrDefault(d => d.Id == "ZAM005");
        Assert.Null(zam005);
    }

    [Fact]
    public void ZAM002_IncludesHandlerNames_InMessage()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandlerA : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("A");
            }

            public class PingHandlerB : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct)
                    => ValueTask.FromResult("B");
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam002 = diagnostics.FirstOrDefault(d => d.Id == "ZAM002");
        Assert.NotNull(zam002);
        var message = zam002.GetMessage();
        Assert.Contains("PingHandlerA", message);
        Assert.Contains("PingHandlerB", message);
    }

    [Fact]
    public void NoDiagnostics_WhenEverythingIsValid()
    {
        var source = """
            using ZeroAlloc.Mediator;
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

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // No ZeroAlloc.Mediator diagnostics at all
        var zamDiagnostics = diagnostics.Where(d => d.Id.StartsWith("ZAM", StringComparison.Ordinal)).ToList();
        Assert.Empty(zamDiagnostics);
    }

    [Fact]
    public void Generator_ReportsZam008_WhenHandlerHasNoParameterlessConstructor()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                private readonly object _dep;
                public PingHandler(object dep) => _dep = dep;
                public ValueTask<string> Handle(Ping r, CancellationToken ct) => default;
            }
            """;
        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam008 = diagnostics.SingleOrDefault(d => d.Id == "ZAM008");
        Assert.NotNull(zam008);
        Assert.Equal(DiagnosticSeverity.Warning, zam008!.Severity);
        Assert.Contains("PingHandler", zam008.GetMessage(null));
    }

    [Fact]
    public void Generator_Zam008_HasHandlerLocation_NotLocationNone()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                private readonly object _dep;
                public PingHandler(object dep) => _dep = dep;
                public ValueTask<string> Handle(Ping r, CancellationToken ct) => default;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam008 = diagnostics.SingleOrDefault(d => d.Id == "ZAM008");
        Assert.NotNull(zam008);
        Assert.NotEqual(Location.None, zam008!.Location);
        Assert.True(zam008.Location.IsInSource);
        // The reported location should be the PingHandler class identifier.
        var sourceText = zam008.Location.SourceTree?.GetText().ToString() ?? "";
        var span = zam008.Location.SourceSpan;
        Assert.Equal("PingHandler", sourceText.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Generator_Zam008_LocationIsInsidePragmaScope()
    {
        // This is the user-visible improvement we are shipping: the diagnostic's
        // location must fall inside the source span between
        // `#pragma warning disable ZAM008` and `#pragma warning restore ZAM008`,
        // otherwise the pragma cannot suppress it.
        //
        // RunGeneratorsAndUpdateCompilation passes the generator's diagnostics through the
        // compilation's filter, which applies the pragma and sets IsSuppressed.
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            #pragma warning disable ZAM008
            public class PingHandler : IRequestHandler<Ping, string>
            {
                private readonly object _dep;
                public PingHandler(object dep) => _dep = dep;
                public ValueTask<string> Handle(Ping r, CancellationToken ct) => default;
            }
            #pragma warning restore ZAM008
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam008 = diagnostics.SingleOrDefault(d => d.Id == "ZAM008");
        Assert.NotNull(zam008);
        Assert.True(zam008!.Location.IsInSource);

        var sourceText = zam008.Location.SourceTree!.GetText().ToString();
        var disableIdx = sourceText.IndexOf("#pragma warning disable ZAM008", StringComparison.Ordinal);
        var restoreIdx = sourceText.IndexOf("#pragma warning restore ZAM008", StringComparison.Ordinal);
        Assert.True(disableIdx >= 0 && restoreIdx > disableIdx, "pragma directives missing in test source");

        var locStart = zam008.Location.SourceSpan.Start;
        Assert.InRange(locStart, disableIdx, restoreIdx);
        Assert.True(zam008.IsSuppressed);
    }

    [Fact]
    public void Generator_DoesNotReportZam008_WhenHandlerHasParameterlessConstructor()
    {
        var source = """
            using ZeroAlloc.Mediator;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping r, CancellationToken ct) => default;
            }
            """;
        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAM008");
    }

    private const string Zam009App = """
        using ZeroAlloc.Mediator;
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

        """;

    private const string Zam009HandleMethod = """
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        """;

    [Fact]
    public void ZAM009_StaticClassBehavior_EmitsWarning_AndStaysOutOfSend()
    {
        var source = Zam009App + $$"""
            [PipelineBehavior(Order = 0)]
            public static class StaticBehavior
            {
            {{Zam009HandleMethod}}
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam009 = Assert.Single(diagnostics, d => d.Id == "ZAM009");
        Assert.Equal(DiagnosticSeverity.Warning, zam009.Severity);
        var message = zam009.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'global::TestApp.StaticBehavior'", message, StringComparison.Ordinal);
        Assert.Contains("make the class non-static and implement IPipelineBehavior", message, StringComparison.Ordinal);
        Assert.Contains("Send(", output, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticBehavior", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAM009_ClassWithoutInterface_EmitsWarning_AndStaysOutOfSend()
    {
        var source = Zam009App + $$"""
            [PipelineBehavior(Order = 0)]
            public sealed class PlainBehavior
            {
            {{Zam009HandleMethod}}
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam009 = Assert.Single(diagnostics, d => d.Id == "ZAM009");
        Assert.Equal(DiagnosticSeverity.Warning, zam009.Severity);
        var message = zam009.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'global::TestApp.PlainBehavior'", message, StringComparison.Ordinal);
        Assert.EndsWith("; implement IPipelineBehavior", message, StringComparison.Ordinal);
        Assert.DoesNotContain("non-static", message, StringComparison.Ordinal);
        Assert.Contains("Send(", output, StringComparison.Ordinal);
        Assert.DoesNotContain("PlainBehavior", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAM009_IsReported_ForTheBasePipelineAttribute()
    {
        // The generator registers ZeroAlloc.Pipeline.PipelineBehaviorAttribute and the
        // ZeroAlloc.Mediator subclass separately; both must report.
        var source = Zam009App + """
            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 0)]
            public static class BaseAttributeBehavior { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        var zam009 = Assert.Single(diagnostics, d => d.Id == "ZAM009");
        Assert.Contains(
            "'global::TestApp.BaseAttributeBehavior'",
            zam009.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ZAM009_IsReportedOnce_ForATypeWithBothAttributes()
    {
        // Each registration yields the type, so without deduplication it would report twice.
        var source = Zam009App + """
            [PipelineBehavior(Order = 0)]
            [ZeroAlloc.Pipeline.PipelineBehavior(Order = 0)]
            public static class DoubleAttributeBehavior { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Single(diagnostics, d => d.Id == "ZAM009");
    }

    [Fact]
    public void ZAM009_NotEmitted_ForAValidBehavior()
    {
        var source = Zam009App + $$"""
            [PipelineBehavior(Order = 0)]
            public sealed class LoggingBehavior : IPipelineBehavior
            {
            {{Zam009HandleMethod}}
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "ZAM009");
        Assert.Contains("LoggingBehavior.Handle", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ZAM009_IsSuppressedByAPragmaAroundTheBehavior()
    {
        var source = Zam009App + """
            #pragma warning disable ZAM009
            [PipelineBehavior(Order = 0)]
            public static class QuietBehavior { }
            #pragma warning restore ZAM009

            [PipelineBehavior(Order = 10)]
            public static class LoudBehavior { }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var zam009 = diagnostics.Where(d => d.Id == "ZAM009").ToList();
        Assert.Equal(2, zam009.Count);
        Assert.True(ForBehavior(zam009, "QuietBehavior").IsSuppressed);
        Assert.False(ForBehavior(zam009, "LoudBehavior").IsSuppressed);

        static Diagnostic ForBehavior(List<Diagnostic> list, string name) =>
            Assert.Single(list, d => d.GetMessage(CultureInfo.InvariantCulture)
                .Contains("TestApp." + name + "'", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_EmitsNoCode_WhenNoHandlers()
    {
        var source = """
            using ZeroAlloc.Mediator;

            namespace TestApp;

            // No handlers, no request types — just the namespace
            public class NotAHandler { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Should still emit the Mediator class shell with Configure
        Assert.Contains("internal static partial class Mediator", output);
        Assert.Contains("Configure", output);

        // But no Send/Publish/CreateStream
        Assert.DoesNotContain("Send(", output);
        Assert.DoesNotContain("Publish(", output);
        Assert.DoesNotContain("CreateStream(", output);
    }
}
