using System;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// Guards issue #100: every type the generator emits into the shared
/// <c>ZeroAlloc.Mediator</c> namespace must be <c>internal</c>.
/// <para>
/// The generated members are derived from the handlers discovered in the assembly being
/// compiled, so each assembly running the generator gets a differently-shaped copy. Emitting
/// them <c>public</c> made those copies visible to one another, and any project referencing two
/// such assemblies failed to compile with CS0436 — analyzer assets flow transitively through
/// ProjectReference, so this hit ordinary multi-project solutions rather than an exotic setup.
/// </para>
/// <para>
/// These types cannot move to the runtime package instead: a shared <c>IMediator</c> would have
/// no members, because there would be no handlers to derive them from. Internal visibility is
/// therefore the design, and this test states it explicitly — the other generator tests only
/// match these declarations incidentally, as anchors for locating output.
/// </para>
/// </summary>
public class GeneratedTypeVisibilityTests
{
    private const string HandlerSource = """
        using ZeroAlloc.Mediator;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace TestApp;

        public readonly record struct Ping(string Message) : IRequest<string>;

        public class PingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken ct)
                => ValueTask.FromResult("Pong");
        }

        public readonly record struct Pinged(string Message) : INotification;

        public class PingedHandler : INotificationHandler<Pinged>
        {
            public ValueTask Handle(Pinged notification, CancellationToken ct) => ValueTask.CompletedTask;
        }
        """;

    [Theory]
    [InlineData("interface IMediator")]
    [InlineData("class MediatorService")]
    [InlineData("class MediatorConfig")]
    [InlineData("class Mediator")]
    [InlineData("class MediatorServiceCollectionExtensions")]
    public void GeneratedTypes_AreInternal(string declaration)
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(HandlerSource);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Asserting the absence of "public … <declaration>" rather than the presence of
        // "internal … <declaration>" catches a new emission site that reintroduces public
        // visibility, which a presence check on the existing site would miss.
        var publicIndex = output.IndexOf("public ", StringComparison.Ordinal);
        while (publicIndex >= 0)
        {
            var lineEnd = output.IndexOf('\n', publicIndex);
            var line = lineEnd < 0
                ? output.Substring(publicIndex)
                : output.Substring(publicIndex, lineEnd - publicIndex);

            Assert.False(
                line.Contains(declaration, StringComparison.Ordinal),
                $"Generated type must be internal (issue #100), found: {line.Trim()}");

            publicIndex = output.IndexOf("public ", publicIndex + 1, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneratedMediatorService_StillImplementsGeneratedInterface()
    {
        // Internal visibility must not change the shape: the concrete service still implements
        // the generated interface, so consumers keep resolving IMediator from DI within their
        // own assembly.
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(HandlerSource);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("internal partial class MediatorService : IMediator", output, StringComparison.Ordinal);
        Assert.Contains("internal partial interface IMediator", output, StringComparison.Ordinal);
    }
}
