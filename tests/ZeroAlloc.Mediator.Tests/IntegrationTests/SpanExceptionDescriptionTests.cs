using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator.Tests.GeneratorTests;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

// The tests below listen to, and measure, the ZeroAlloc.Mediator activity source. Every assembly
// that runs the generator emits on that one source, so a span-capturing test running in parallel
// would put its spans, and their allocations, into these measurements. This collection runs alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MediatorActivitySourceCollection
{
    public const string Name = "ZeroAlloc.Mediator activity source";
}

// #292: [assembly: MediatorTracing(ExceptionDescription = false)] keeps the exception message out
// of the generated dispatch spans. The attribute applies to the assembly the generator runs in, so
// each setting needs its own compiled assembly: these tests generate, compile and load one per
// setting, then drive its generated Mediator and IMediator the way a consumer would.
[Collection(MediatorActivitySourceCollection.Name)]
public class SpanExceptionDescriptionTests
{
    private const string ProbeSource = """
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using ZeroAlloc.Mediator;

        // ATTRIBUTE

        namespace SpanProbe;

        public readonly record struct Ok(int Value) : IRequest<int>;

        [HandlerLifetime(ServiceLifetime.Singleton)]
        public sealed class OkHandler : IRequestHandler<Ok, int>
        {
            public ValueTask<int> Handle(Ok request, CancellationToken ct) => ValueTask.FromResult(request.Value + 1);
        }

        public readonly record struct FailingRequest(int Value) : IRequest<int>;

        public sealed class FailingRequestHandler : IRequestHandler<FailingRequest, int>
        {
            public ValueTask<int> Handle(FailingRequest request, CancellationToken ct)
                => throw new TimeoutException("secret request text");
        }

        public readonly record struct FailingNotification(int Value) : INotification;

        public sealed class FailingNotificationHandler : INotificationHandler<FailingNotification>
        {
            public ValueTask Handle(FailingNotification notification, CancellationToken ct)
                => throw new InvalidOperationException("secret notification text");
        }

        public readonly record struct FailingStream(int Max) : IStreamRequest<int>;

        public sealed class FailingStreamHandler : IStreamRequestHandler<FailingStream, int>
        {
            public FailingStreamHandler() => throw new ArgumentException("secret stream text");

            public async IAsyncEnumerable<int> Handle(FailingStream request, [EnumeratorCancellation] CancellationToken ct)
            {
                await Task.Yield();
                yield return request.Max;
            }
        }

        public static class Entry
        {
            private static readonly OkHandler s_ok = new();

            public static Func<int, ValueTask<int>> SendOk(bool viaDi)
            {
                if (!viaDi)
                {
                    // A cached handler, so the measurement holds Send alone rather than the
                    // parameterless-constructor fallback's new handler per call.
                    Mediator.Configure(static c => c.SetFactory(static () => s_ok));
                    return static v => Mediator.Send(new Ok(v));
                }

                var mediator = Provider().GetRequiredService<IMediator>();
                return v => mediator.Send(new Ok(v), CancellationToken.None);
            }

            public static Func<Task> Fail(string kind, bool viaDi)
            {
                var mediator = viaDi ? Provider().GetRequiredService<IMediator>() : null;
                return kind switch
                {
                    "send" => async () =>
                    {
                        if (mediator is null) await Mediator.Send(new FailingRequest(1));
                        else await mediator.Send(new FailingRequest(1), CancellationToken.None);
                    },
                    "publish" => async () =>
                    {
                        if (mediator is null) await Mediator.Publish(new FailingNotification(1));
                        else await mediator.Publish(new FailingNotification(1), CancellationToken.None);
                    },
                    "stream" => () =>
                    {
                        _ = mediator is null
                            ? Mediator.CreateStream(new FailingStream(1))
                            : mediator.CreateStream(new FailingStream(1), CancellationToken.None);
                        return Task.CompletedTask;
                    },
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                };
            }

            private static IServiceProvider Provider()
            {
                var services = new ServiceCollection();
                services.AddMediator();
                return services.BuildServiceProvider();
            }
        }
        """;

    private static readonly Lazy<Assembly> WithDescription = new(() => CompileProbe(exceptionDescription: true));
    private static readonly Lazy<Assembly> WithoutDescription = new(() => CompileProbe(exceptionDescription: false));

    public static TheoryData<string, string, string, string, bool> Failures()
    {
        var data = new TheoryData<string, string, string, string, bool>();
        foreach (var viaDi in new[] { false, true })
        {
            data.Add("send", "mediator.send", "request.type", "FailingRequest", viaDi);
            data.Add("publish", "mediator.publish", "notification.type", "FailingNotification", viaDi);
            data.Add("stream", "mediator.stream", "request.type", "FailingStream", viaDi);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ExceptionDescriptionFalse_FailedSpan_HasErrorStatusAndType_ButNoMessage(
        string kind, string spanName, string tag, string typeName, bool viaDi)
    {
        var activity = await CaptureFailedSpan(WithoutDescription.Value, kind, spanName, tag, typeName, viaDi);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Null(activity.StatusDescription);
        Assert.NotNull(activity.GetTagItem("error.type"));
        Assert.DoesNotContain(activity.TagObjects, t => t.Value is string s && s.Contains("secret", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ExceptionDescriptionTrue_FailedSpan_KeepsTheMessage(
        string kind, string spanName, string tag, string typeName, bool viaDi)
    {
        var activity = await CaptureFailedSpan(WithDescription.Value, kind, spanName, tag, typeName, viaDi);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.StartsWith("secret ", activity.StatusDescription, StringComparison.Ordinal);
        Assert.NotNull(activity.GetTagItem("error.type"));
    }

    // The opt-out changes only the catch, so a Send that succeeds must still allocate nothing in
    // both builds: the whole public call, static and through IMediator, with no listener attached.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Send_ThatSucceeds_AllocatesNothing(bool exceptionDescription, bool viaDi)
    {
        var probe = exceptionDescription ? WithDescription.Value : WithoutDescription.Value;
        var send = Entry<Func<int, ValueTask<int>>>(probe, "SendOk", viaDi);

        AllocationGate.AssertBudgetValueTask(0, 1000, () => send(41),
            $"{(viaDi ? "IMediator" : "Mediator")}.Send, ExceptionDescription = {exceptionDescription}");
    }

    private static async Task<Activity> CaptureFailedSpan(
        Assembly probe, string kind, string spanName, string tag, string typeName, bool viaDi)
    {
        var fail = Entry<Func<Task>>(probe, "Fail", kind, viaDi);
        var stopped = new ConcurrentQueue<Activity>();
        using (var listener = new ActivityListener
        {
            ShouldListenTo = src => string.Equals(src.Name, "ZeroAlloc.Mediator", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        })
        {
            ActivitySource.AddActivityListener(listener);
            var thrown = await Assert.ThrowsAnyAsync<Exception>(fail).ConfigureAwait(false);
            Assert.StartsWith("secret ", thrown.Message, StringComparison.Ordinal);
        }

        return Assert.Single(stopped, a => string.Equals(a.OperationName, spanName, StringComparison.Ordinal)
                                        && Equals(a.GetTagItem(tag), typeName));
    }

    private static T Entry<T>(Assembly probe, string method, params object[] arguments)
        => (T)probe.GetType("SpanProbe.Entry", throwOnError: true)!
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, arguments)!;

    private static Assembly CompileProbe(bool exceptionDescription)
    {
        // The probe calls BuildServiceProvider, whose assembly nothing may have loaded yet, so it
        // is referenced explicitly rather than found among the AppDomain's loaded assemblies.
        var references = GeneratorTestHelper.CompilableReferences();
        references.Add(MetadataReference.CreateFromFile(typeof(ServiceProvider).Assembly.Location));

        var attribute = $"[assembly: ZeroAlloc.Mediator.MediatorTracing(ExceptionDescription = {(exceptionDescription ? "true" : "false")})]\n";
        var compilation = CSharpCompilation.Create(
            exceptionDescription ? "SpanProbe.WithDescription" : "SpanProbe.WithoutDescription",
            [CSharpSyntaxTree.ParseText(ProbeSource.Replace("// ATTRIBUTE", attribute, StringComparison.Ordinal))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new Generator.MediatorGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var image = new MemoryStream();
        var result = output.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return Assembly.Load(image.ToArray());
    }
}
