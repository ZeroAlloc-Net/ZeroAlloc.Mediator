using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

internal static class GeneratorTestHelper
{
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
        => RunGenerator(source, []);

    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(
        string source, IEnumerable<MetadataReference> additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = BaseReferences();
        references.AddRange(additionalReferences);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.MediatorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZeroAlloc"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }

    /// <summary>The file path given to the source tree by <see cref="RunGeneratorOnFile"/>.</summary>
    public const string TestFilePath = "/src/App.cs";

    /// <summary>
    /// Runs the generator on a source tree that has the file path <see cref="TestFilePath"/>, so a
    /// test can assert the file of a diagnostic's location. The returned diagnostics went through
    /// the compilation's filter, so a diagnostic inside <c>#pragma warning disable</c> has
    /// <see cref="Diagnostic.IsSuppressed"/> set.
    /// </summary>
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorOnFile(string source)
    {
        var compilation = CreateCompilation([CSharpSyntaxTree.ParseText(source, path: TestFilePath)]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.MediatorGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var output = string.Join("\n", outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZeroAlloc"))
            .Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }

    public static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees) =>
        CSharpCompilation.Create(
            "TestAssembly",
            trees,
            BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // Every loaded assembly, minus this repo's own apart from the ZeroAlloc.Mediator runtime.
    // The generator puts public pipeline behaviors from referenced assemblies into the pipeline,
    // so letting the bridge packages, this test assembly or the samples in would add their
    // behaviors to every test's output. A test that wants a referenced behavior passes it in.
    public static List<MetadataReference> BaseReferences()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => !(a.GetName().Name ?? string.Empty).StartsWith("ZeroAlloc.Mediator.", StringComparison.Ordinal))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(IRequest<>).Assembly.Location));
        return references;
    }
}
