using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Mediator.Tests.GeneratorTests;

/// <summary>
/// The generator's cached models carry source locations, so an edit that touches no handler,
/// request or behavior must leave every step and output cached, and an edit that moves a handler
/// must move its diagnostic with it.
/// </summary>
public class IncrementalityTests
{
    private const string AppSource = """
        using ZeroAlloc.Mediator;
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TestApp;

        public readonly record struct Ping : IRequest<string>;
        public class Pong : IRequest<string> { }
        public readonly record struct Orphan : IRequest<string>;
        public readonly record struct Joined : INotification;

        public class PingHandler : IRequestHandler<Ping, string>
        {
            public PingHandler(object dep) { }
            public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
        }

        public class PongHandler : IRequestHandler<Pong, string>
        {
            public ValueTask<string> Handle(Pong request, CancellationToken ct) => default;
        }

        public class JoinedHandler : INotificationHandler<Joined>
        {
            public ValueTask Handle(Joined notification, CancellationToken ct) => default;
        }

        public readonly record struct Ticks : IStreamRequest<int>;

        public class TicksHandler : IStreamRequestHandler<Ticks, int>
        {
            public System.Collections.Generic.IAsyncEnumerable<int> Handle(Ticks request, CancellationToken ct)
                => throw new NotSupportedException();
        }

        [PipelineBehavior(Order = 0)]
        public class BadBehavior : IPipelineBehavior { }

        [PipelineBehavior(Order = 5)]
        public static class StaticBehavior { }

        [ZeroAlloc.Mediator.Cache.CacheResponse]
        public class Cached : IRequest<string> { public int Id { get; init; } }

        public class CachedHandler : IRequestHandler<Cached, string>
        {
            public ValueTask<string> Handle(Cached request, CancellationToken ct) => default;
        }

        [ZeroAlloc.Mediator.Cache.CacheResponse]
        public readonly record struct CachedIds(int[] Ids) : IRequest<string>;

        public class CachedIdsHandler : IRequestHandler<CachedIds, string>
        {
            public ValueTask<string> Handle(CachedIds request, CancellationToken ct) => default;
        }
        """;

    // Stands in for ZeroAlloc.Mediator.Cache, which the base references leave out so its behavior
    // stays out of the emitted pipeline. The generator matches the attribute by its name.
    private const string CacheAttributeSource = """
        namespace ZeroAlloc.Mediator.Cache;
        public sealed class CacheResponseAttribute : System.Attribute { }
        """;

    // The tracking names the generator gives its steps, in ZeroAlloc.Mediator.Generator.TrackingNames.
    private static readonly string[] GeneratorStepNames =
    [
        "RequestHandlers", "NotificationHandlers", "StreamHandlers", "SourceBehaviors",
        "RequestTypes", "NotificationTypes", "CachedRequestKeys", "CachedRecordMemberKeys", "EmitInputs",
        "DiagnosticInputs",
    ];

    [Fact]
    public void UnrelatedEdit_LeavesEveryTrackedStepAndOutputCached()
    {
        var app = CSharpSyntaxTree.ParseText(AppSource, path: "/src/App.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace TestApp; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var cache = CSharpSyntaxTree.ParseText(CacheAttributeSource, path: "/src/Cache.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([app, unrelated, cache]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Generator.MediatorGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace TestApp; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        // Roslyn's own steps, such as the one that pairs each tree with the compilation, rerun on
        // every edit; the generator's named steps must not.
        foreach (var name in GeneratorStepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var step in second.TrackedOutputSteps)
        {
            AssertAllCachedOrUnchanged(step.Key, step.Value);
        }

        // A cached output still reports its diagnostics, at the same place.
        Assert.Equal(Describe(first.Diagnostics), Describe(second.Diagnostics));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAM008", StringComparison.Ordinal));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAM009", StringComparison.Ordinal));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAM010", StringComparison.Ordinal));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAM011", StringComparison.Ordinal));
    }

    [Fact]
    public void EditAboveAHandler_MovesItsDiagnostic_AndLeavesTheEmittedSourceCached()
    {
        var app = CSharpSyntaxTree.ParseText(AppSource, path: "/src/App.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([app]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Generator.MediatorGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var before = Assert.Single(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAM008", StringComparison.Ordinal));

        var moved = app.WithChangedText(SourceText.From(
            AppSource.Replace("namespace TestApp;", "namespace TestApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(app, moved));
        var result = driver.GetRunResult();
        var after = Assert.Single(result.Diagnostics, d => string.Equals(d.Id, "ZAM008", StringComparison.Ordinal));

        // Before the fix the cached model kept the first run's location, in a tree that is no
        // longer part of the compilation.
        Assert.Same(moved, after.Location.SourceTree);
        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);

        // Only the locations moved, so the emitted source is not regenerated.
        AssertAllCachedOrUnchanged("EmitInputs", result.Results[0].TrackedSteps["EmitInputs"]);
    }

    [Fact]
    public void HandlerLifetimeEdit_RegeneratesTheServiceCollectionSource()
    {
        const string source = """
            using ZeroAlloc.Mediator;
            using Microsoft.Extensions.DependencyInjection;
            using System.Threading;
            using System.Threading.Tasks;
            namespace TestApp;

            public readonly record struct Ping : IRequest<string>;

            [HandlerLifetime(ServiceLifetime.Scoped)]
            public class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => default;
            }
            """;
        const string hintName = "ZeroAlloc.Mediator.ServiceCollection.g.cs";
        const string registration =
            "services.TryAdd(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::TestApp.PingHandler), "
            + "typeof(global::TestApp.PingHandler), global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.";

        var app = CSharpSyntaxTree.ParseText(source, path: "/src/App.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([app]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Generator.MediatorGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var before = GeneratedText(driver.GetRunResult(), hintName);

        var edited = app.WithChangedText(SourceText.From(
            source.Replace("ServiceLifetime.Scoped", "ServiceLifetime.Singleton", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(app, edited));
        var after = GeneratedText(driver.GetRunResult(), hintName);

        Assert.Contains(registration + "Scoped));", before, StringComparison.Ordinal);
        Assert.False(string.Equals(before, after, StringComparison.Ordinal), $"{hintName} did not change.");
        Assert.Contains(registration + "Singleton));", after, StringComparison.Ordinal);
        Assert.DoesNotContain(registration + "Scoped));", after, StringComparison.Ordinal);
    }

    private static string GeneratedText(GeneratorDriverRunResult result, string hintName) =>
        Assert.Single(result.Results[0].GeneratedSources, s => string.Equals(s.HintName, hintName, StringComparison.Ordinal))
            .SourceText.ToString();

    private static void AssertAllCachedOrUnchanged(string stepName, ImmutableArray<IncrementalGeneratorRunStep> runSteps)
    {
        foreach (var runStep in runSteps)
        {
            foreach (var (_, reason) in runStep.Outputs)
            {
                Assert.True(
                    reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                    $"Step '{stepName}' produced output with reason {reason} after an unrelated edit.");
            }
        }
    }

    private static List<string> Describe(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan()}").OrderBy(s => s, StringComparer.Ordinal).ToList();
}
