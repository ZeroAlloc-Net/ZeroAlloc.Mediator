using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

// #292: [assembly: MediatorTracing(ExceptionDescription = false)] keeps the exception message out
// of every generated dispatch span. Without the attribute, or with it set to true, the span keeps
// the message as its status description, as it always has.
public class SpanExceptionDescriptionGeneratorTests
{
    // One of each dispatch kind, on both the static Mediator and MediatorService, so every
    // emitted span catch is in the output: Send, sequential Publish, parallel Publish and
    // CreateStream.
    private const string Handlers = """
        using ZeroAlloc.Mediator;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        // ATTRIBUTE

        namespace TestApp;

        public readonly record struct Ping(int Value) : IRequest<int>;

        public class PingHandler : IRequestHandler<Ping, int>
        {
            public ValueTask<int> Handle(Ping request, CancellationToken ct) => ValueTask.FromResult(request.Value);
        }

        public readonly record struct Pinged(int Value) : INotification;

        public class PingedHandler : INotificationHandler<Pinged>
        {
            public ValueTask Handle(Pinged notification, CancellationToken ct) => ValueTask.CompletedTask;
        }

        [ParallelNotification]
        public readonly record struct Fanned(int Value) : INotification;

        public class FannedHandler : INotificationHandler<Fanned>
        {
            public ValueTask Handle(Fanned notification, CancellationToken ct) => ValueTask.CompletedTask;
        }

        public readonly record struct Count(int Max) : IStreamRequest<int>;

        public class CountHandler : IStreamRequestHandler<Count, int>
        {
            public async IAsyncEnumerable<int> Handle(Count request, [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                yield return request.Max;
            }
        }
        """;

    private const string CatchClause = "catch (global::System.Exception __ex)";
    private const string StatusWithMessage =
        "__activity.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, __ex.Message);";
    private const string StatusWithoutMessage =
        "__activity.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error);";
    private const string ErrorTypeTag =
        "__activity.SetTag(\"error.type\", __ex.GetType().FullName ?? \"_OTHER\");";

    // Send, Publish, parallel Publish and CreateStream, once on Mediator and once on MediatorService.
    private const int ExpectedSpanCatches = 8;

    public static TheoryData<string> DefaultAttributes => new()
    {
        "",
        "[assembly: ZeroAlloc.Mediator.MediatorTracing]",
        "[assembly: ZeroAlloc.Mediator.MediatorTracing(ExceptionDescription = true)]",
    };

    [Theory]
    [MemberData(nameof(DefaultAttributes))]
    public void ByDefault_EverySpanCatch_SetsTheExceptionMessageAsStatusDescription(string attribute)
    {
        var output = Generate(attribute);

        Assert.Equal(ExpectedSpanCatches, Occurrences(output, CatchClause));
        Assert.Equal(ExpectedSpanCatches, Occurrences(output, StatusWithMessage));
        Assert.Equal(ExpectedSpanCatches, Occurrences(output, ErrorTypeTag));
        Assert.Equal(0, Occurrences(output, StatusWithoutMessage));
    }

    [Fact]
    public void ExceptionDescriptionFalse_NoSpanCatch_ReadsTheExceptionMessage()
    {
        var output = Generate("[assembly: ZeroAlloc.Mediator.MediatorTracing(ExceptionDescription = false)]");

        Assert.Equal(ExpectedSpanCatches, Occurrences(output, CatchClause));
        Assert.Equal(ExpectedSpanCatches, Occurrences(output, StatusWithoutMessage));
        Assert.Equal(ExpectedSpanCatches, Occurrences(output, ErrorTypeTag));
        Assert.DoesNotContain("__ex.Message", output, StringComparison.Ordinal);
    }

    private static string Generate(string attribute)
    {
        var (output, generatorDiagnostics, compilationDiagnostics) =
            GeneratorTestHelper.RunGeneratorAndCompile(Handlers.Replace("// ATTRIBUTE", attribute, StringComparison.Ordinal));

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compilationDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return output;
    }

    private static int Occurrences(string text, string value)
        => Regex.Matches(text, Regex.Escape(value), RegexOptions.None, TimeSpan.FromSeconds(5)).Count;
}
