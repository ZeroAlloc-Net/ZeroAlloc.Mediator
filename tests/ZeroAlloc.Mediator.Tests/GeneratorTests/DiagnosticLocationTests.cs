using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// Every ZAM diagnostic is reported at the type or attribute it is about, as a location bound to
/// the syntax tree, so the IDE can point at it and <c>#pragma warning disable</c> can suppress it.
/// A source marked with [| and |] gives the expected spans; the markers are removed before it runs.
/// </summary>
public class DiagnosticLocationTests
{
    private const string Usings = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TestApp;

        """;

    private const string HandleMethod = """
            public static ValueTask<TResponse> Handle<TRequest, TResponse>(
                TRequest request, CancellationToken ct,
                Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
                where TRequest : IRequest<TResponse>
                => next(request, ct);
        """;

    [Theory]
    // ZAM001: the request type without a handler.
    [InlineData("ZAM001", Usings + """
        public readonly record struct [|Orphan|](string Data) : IRequest<string>;
        """)]
    // ZAM003: the request type that is a class.
    [InlineData("ZAM003", Usings + """
        public class [|Ping|] : IRequest<string> { }
        public class PingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
        }
        """)]
    // ZAM005: the behavior class without a Handle method.
    [InlineData("ZAM005", Usings + """
        [PipelineBehavior(Order = 0)]
        public class [|BadBehavior|] : IPipelineBehavior { }
        """)]
    // ZAM008: the handler class without a parameterless constructor.
    [InlineData("ZAM008", Usings + """
        public readonly record struct Ping : IRequest<string>;
        public class [|PingHandler|] : IRequestHandler<Ping, string>
        {
            public PingHandler(object dep) { }
            public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
        }
        """)]
    public void Diagnostic_IsReportedAtItsSourceLocation(string id, string markedSource)
    {
        var (source, spans) = Unmark(markedSource);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = One(diagnostics, id);
        AssertAt(diagnostic.Location, source, Assert.Single(spans));
    }

    [Fact]
    public void ZAM003_ForARequestDeclaredElsewhere_IsReportedAtTheHandler()
    {
        // The request type lives in a referenced assembly, so the handler is the code in this
        // compilation that dispatches it.
        var (source, spans) = Unmark(Usings + """
            public class [|ExternalHandler|] : IRequestHandler<ExternalRequest, string>
            {
                public ValueTask<string> Handle(ExternalRequest request, CancellationToken ct) => default;
            }
            """);
        var requests = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "Requests",
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                    "namespace TestApp; public class ExternalRequest : ZeroAlloc.Mediator.IRequest<string> { }")],
                GeneratorTestHelper.BaseReferences(),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .ToMetadataReference();

        var compilation = GeneratorTestHelper.CreateCompilation(
                [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, path: GeneratorTestHelper.TestFilePath)])
            .AddReferences(requests);
        GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver.Create(new Generator.MediatorGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        AssertAt(One(diagnostics, "ZAM003").Location, source, Assert.Single(spans));
    }

    [Fact]
    public void ZAM002_IsReportedAtTheLaterHandler_WithTheEarlierAsAdditionalLocation()
    {
        var (source, spans) = Unmark(Usings + """
            public readonly record struct Ping : IRequest<string>;
            public class [|PingHandler1|] : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
            }
            public class [|PingHandler2|] : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
            }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = One(diagnostics, "ZAM002");
        AssertAt(diagnostic.Location, source, spans[1]);
        AssertAt(Assert.Single(diagnostic.AdditionalLocations), source, spans[0]);
    }

    [Fact]
    public void ZAM006_IsReportedAtTheLaterAttribute_WithTheEarlierAsAdditionalLocation()
    {
        var (source, spans) = Unmark(Usings + $$"""
            [[|PipelineBehavior(Order = 1)|]]
            public class BehaviorA : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            [[|PipelineBehavior(Order = 1)|]]
            public class BehaviorB : IPipelineBehavior
            {
            {{HandleMethod}}
            }
            """);

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var diagnostic = One(diagnostics, "ZAM006");
        AssertAt(diagnostic.Location, source, spans[1]);
        AssertAt(Assert.Single(diagnostic.AdditionalLocations), source, spans[0]);
    }

    [Fact]
    public void PragmaAroundOneRequest_SuppressesThatDiagnosticOnly()
    {
        // Both requests are classes and report ZAM003; the pragma covers Quiet only. Quiet's
        // handler has no parameterless constructor, so ZAM008 fires inside the same region, and
        // the pragma does not name it.
        var source = Usings + """
            #pragma warning disable ZAM003
            public class QuietRequest : IRequest<string> { }
            public class QuietHandler : IRequestHandler<QuietRequest, string>
            {
                public QuietHandler(object dep) { }
                public ValueTask<string> Handle(QuietRequest request, CancellationToken ct) => default;
            }
            #pragma warning restore ZAM003

            public class LoudRequest : IRequest<string> { }
            public class LoudHandler : IRequestHandler<LoudRequest, string>
            {
                public ValueTask<string> Handle(LoudRequest request, CancellationToken ct) => default;
            }
            """;

        var (_, diagnostics) = GeneratorTestHelper.RunGeneratorOnFile(source);

        var zam003 = diagnostics.Where(d => string.Equals(d.Id, "ZAM003", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, zam003.Count);
        Assert.True(ForType(zam003, "QuietRequest").IsSuppressed);
        Assert.False(ForType(zam003, "LoudRequest").IsSuppressed);
        Assert.False(One(diagnostics, "ZAM008").IsSuppressed);

        static Diagnostic ForType(List<Diagnostic> list, string typeName) =>
            Assert.Single(list, d => d.GetMessage(CultureInfo.InvariantCulture)
                .Contains("TestApp." + typeName + "'", StringComparison.Ordinal));
    }

    internal static Diagnostic One(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.Single(diagnostics, d => string.Equals(d.Id, id, StringComparison.Ordinal));

    internal static void AssertAt(Location location, string source, TextSpan expected)
    {
        // A source location, bound to the tree, is what #pragma and the IDE need.
        Assert.Equal(LocationKind.SourceFile, location.Kind);
        Assert.Equal(GeneratorTestHelper.TestFilePath, location.SourceTree!.FilePath);
        Assert.Equal(expected, location.SourceSpan);
        Assert.Equal(SourceText.From(source).Lines.GetLinePositionSpan(expected), location.GetLineSpan().Span);
    }

    internal static (string source, List<TextSpan> spans) Unmark(string marked)
    {
        var sb = new StringBuilder(marked.Length);
        var spans = new List<TextSpan>();
        var start = -1;
        for (var i = 0; i < marked.Length; i++)
        {
            if (string.CompareOrdinal(marked, i, "[|", 0, 2) == 0)
            {
                start = sb.Length;
                i++;
            }
            else if (string.CompareOrdinal(marked, i, "|]", 0, 2) == 0)
            {
                spans.Add(TextSpan.FromBounds(start, sb.Length));
                i++;
            }
            else
            {
                sb.Append(marked[i]);
            }
        }

        return (sb.ToString(), spans);
    }
}
