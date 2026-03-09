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

    [Fact]
    public void Generator_EmitsMultipleCreateStream_ForMultipleStreamTypes()
    {
        var source = """
            using ZMediator;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;

            namespace TestApp;

            public readonly record struct CountRequest(int Max) : IStreamRequest<int>;
            public readonly record struct NamesRequest : IStreamRequest<string>;

            public class CountHandler : IStreamRequestHandler<CountRequest, int>
            {
                public async IAsyncEnumerable<int> Handle(
                    CountRequest request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                }
            }

            public class NamesHandler : IStreamRequestHandler<NamesRequest, string>
            {
                public async IAsyncEnumerable<string> Handle(
                    NamesRequest request,
                    [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return "Alice";
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("CreateStream(global::TestApp.CountRequest request", output);
        Assert.Contains("CreateStream(global::TestApp.NamesRequest request", output);
        Assert.Contains("IAsyncEnumerable<int>", output);
        Assert.Contains("IAsyncEnumerable<string>", output);
    }

    [Fact]
    public void Generator_StreamHandler_HasFactoryField()
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
                    yield return 1;
                }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("_countHandlerFactory", output);
        Assert.Contains("Func<global::TestApp.CountHandler>", output);
    }
}
