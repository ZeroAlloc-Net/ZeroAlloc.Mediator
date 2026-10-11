using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZeroAlloc.Mediator.Cache;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// ZAM010: CacheBehavior keys an entry by the request's text, so a [CacheResponse] request whose
/// text is its type name puts every instance under one entry and answers each with the first
/// response.
/// </summary>
public class CacheKeyDiagnosticTests
{
    private const string Usings = """
        using ZeroAlloc.Mediator;
        using ZeroAlloc.Mediator.Cache;
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TestApp;

        """;

    [Fact]
    public void PlainClass_IsReportedAsAnError_AtTheTypeName()
    {
        var (source, spans) = DiagnosticLocationTests.Unmark(Usings + """
            [CacheResponse(TtlMs = 1000)]
            public sealed class [|GetUser|] : IRequest<int> { public int Id { get; init; } }
            """ + Handler("GetUser"));

        var diagnostics = Run(source);

        var zam010 = DiagnosticLocationTests.One(diagnostics, "ZAM010");
        Assert.Equal(DiagnosticSeverity.Error, zam010.Severity);
        Assert.Contains("TestApp.GetUser'", zam010.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        DiagnosticLocationTests.AssertAt(zam010.Location, source, Assert.Single(spans));
    }

    [Theory]
    // A struct that is not a record formats as its type name too.
    [InlineData("public struct R : IRequest<int> { public int Id; }")]
    // The state that varies may sit in a base class.
    [InlineData("""
        public abstract class Paged { public int Page { get; init; } }
        [CacheResponse(TtlMs = 1000)]
        public sealed class R : Paged, IRequest<int> { }
        """)]
    // A captured primary constructor parameter is state the type name does not show.
    [InlineData("public sealed class R(int id) : IRequest<int> { public int Id => id; }")]
    // A property with a backing field it names through the field keyword.
    [InlineData("public sealed class R : IRequest<int> { public int Id { get => field; set => field = value; } }")]
    public void TypeWithStateAndNoValueText_IsReported(string declaration)
    {
        var source = Usings + Attributed(declaration) + Handler("R");

        var diagnostics = Run(source);

        Assert.Single(diagnostics, d => string.Equals(d.Id, "ZAM010", StringComparison.Ordinal));
    }

    [Theory]
    // The compiler writes a ToString that prints the members.
    [InlineData("public sealed record R(int Id) : IRequest<int>;")]
    [InlineData("public readonly record struct R(int Id) : IRequest<int>;")]
    // The type writes its own.
    [InlineData("public sealed class R : IRequest<int> { public int Id { get; init; } public override string ToString() => Id.ToString(); }")]
    [InlineData("public struct R : IRequest<int> { public int Id; public override readonly string ToString() => Id.ToString(); }")]
    // A base class writes it.
    [InlineData("""
        public abstract class Keyed { public int Id { get; init; } public override string ToString() => Id.ToString(); }
        [CacheResponse(TtlMs = 1000)]
        public sealed class R : Keyed, IRequest<int> { }
        """)]
    // The key interpolation formats an IFormattable or ISpanFormattable through it, not ToString.
    [InlineData("""
        public sealed class R : IRequest<int>, ISpanFormattable
        {
            public int Id { get; init; }
            public string ToString(string? format, IFormatProvider? provider) => Id.ToString(format, provider);
            public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
                => Id.TryFormat(destination, out charsWritten, format, provider);
        }
        """)]
    [InlineData("""
        public sealed class R : IRequest<int>, IFormattable
        {
            public int Id { get; init; }
            public string ToString(string? format, IFormatProvider? provider) => Id.ToString(format, provider);
        }
        """)]
    // No instance state: every instance is the same request, so one entry is right.
    [InlineData("public sealed class R : IRequest<int> { }")]
    [InlineData("public sealed class R : IRequest<int> { public const int Size = 10; public static int Shared; public int Computed => Size; }")]
    [InlineData("public struct R : IRequest<int> { }")]
    public void TypeWithAValueKey_IsNotReported(string declaration)
    {
        var source = Usings + Attributed(declaration) + Handler("R");

        var diagnostics = Run(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAM010", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassWithoutCacheResponse_IsNotReported()
    {
        var source = Usings + "public sealed class R : IRequest<int> { public int Id { get; init; } }" + Handler("R");

        var diagnostics = Run(source);

        Assert.DoesNotContain(diagnostics, d => string.Equals(d.Id, "ZAM010", StringComparison.Ordinal));
    }

    [Fact]
    public void Pragma_SuppressesIt()
    {
        var source = Usings + """
            #pragma warning disable ZAM010
            [CacheResponse(TtlMs = 1000)]
            public sealed class R : IRequest<int> { public int Id { get; init; } }
            #pragma warning restore ZAM010
            """ + Handler("R");

        var diagnostics = Run(source);

        Assert.True(DiagnosticLocationTests.One(diagnostics, "ZAM010").IsSuppressed);
    }

    // Puts [CacheResponse] on a single-line declaration; a multi-line one carries its own.
    private static string Attributed(string declaration) =>
        declaration.Contains("[CacheResponse", StringComparison.Ordinal)
            ? declaration
            : "[CacheResponse(TtlMs = 1000)]\n" + declaration;

    private static string Handler(string request) => $$"""

        public sealed class {{request}}Handler : IRequestHandler<{{request}}, int>
        {
            public ValueTask<int> Handle({{request}} request, CancellationToken ct) => default;
        }
        """;

    // The generator output goes through the compilation's filter, so a #pragma marks it suppressed.
    internal static ImmutableArray<Diagnostic> Run(string source, params MetadataReference[] extraReferences)
    {
        var references = GeneratorTestHelper.CompilableReferences();
        references.Add(MetadataReference.CreateFromFile(typeof(CacheResponseAttribute).Assembly.Location));
        references.AddRange(extraReferences);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path: GeneratorTestHelper.TestFilePath)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Generator.MediatorGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        // The test sources must compile, or a case could pass for the wrong reason.
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZAM", StringComparison.Ordinal)));
        return diagnostics;
    }
}
