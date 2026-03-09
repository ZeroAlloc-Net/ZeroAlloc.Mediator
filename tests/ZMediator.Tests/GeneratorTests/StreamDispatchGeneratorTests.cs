using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ZMediator.Tests.GeneratorTests;

public class StreamDispatchGeneratorTests
{
    [Fact]
    public void Generator_EmitsCreateStream_ForStreamHandler()
    {
        var source = """
            using ZMediator;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;

            namespace TestApp;

            public readonly record struct CountRequest(int Max) : IStreamRequest<int>;

            public class CountHandler : IStreamRequestHandler<CountRequest, int>
            {
                public async IAsyncEnumerable<int> Handle(
                    CountRequest request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    for (var i = 0; i < request.Max; i++)
                        yield return i;
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static System.Collections.Generic.IAsyncEnumerable<int> CreateStream(global::TestApp.CountRequest request", output);
    }
}
