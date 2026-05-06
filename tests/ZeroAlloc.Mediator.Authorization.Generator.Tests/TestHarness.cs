using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mediator.Authorization.Generator.Tests;

internal static class TestHarness
{
    public static GeneratorRunResult Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            ReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new AuthorizationGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return runResult.Results.Single();
    }

    public static string RunGenerator(string source)
    {
        var result = Run(source);
        if (result.GeneratedSources.Length == 0)
        {
            return "// (no files emitted)";
        }

        return string.Join(
            "\n// ===== next file =====\n",
            result.GeneratedSources.Select(s => $"// {s.HintName}\n{s.SourceText}"));
    }

    public static ImmutableArray<Diagnostic> RunDiagnostics(string source) => Run(source).Diagnostics;

    private static IEnumerable<MetadataReference> ReferenceAssemblies()
    {
        // Force-load the contract + runtime assemblies so they appear in the AppDomain.
        // Without this, the reference set is missing ZeroAlloc.Authorization and ZeroAlloc.Mediator,
        // and the test compilation cannot semantic-bind the [Authorize] / IRequest<> types.
        _ = typeof(ZeroAlloc.Authorization.AuthorizeAttribute).FullName;
        _ = typeof(ZeroAlloc.Mediator.IRequest<>).FullName;
        _ = typeof(ZeroAlloc.Mediator.Authorization.IAuthorizedRequest<>).FullName;

        return System.AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));
    }
}
