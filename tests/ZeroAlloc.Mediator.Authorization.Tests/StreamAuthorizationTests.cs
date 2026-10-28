using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Authorization;
using Xunit;

namespace ZeroAlloc.Mediator.Authorization.Tests;

// Stream requests carrying [RequirePolicy]. Pipeline behaviors do not wrap CreateStream, so the
// generated CreateStream calls AuthorizationBehavior.CheckStream before the handler (#387).
[RequirePolicy("IntegrationTest")]
public readonly record struct SecuredStreamRequest(int Count) : IStreamRequest<int>;

[RequirePolicy("AlwaysDeny")]
public readonly record struct DeniedStreamRequest(int Count) : IStreamRequest<int>;

// No [RequirePolicy]: no dispatcher is emitted for it, so nothing is checked.
public readonly record struct OpenStreamRequest(int Count) : IStreamRequest<int>;

// How often each handler's Handle was called. Tests are non-parallel, so one shared table is safe.
internal static class StreamCalls
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> s_calls = new(StringComparer.Ordinal);

    public static void Reset() => s_calls.Clear();

    public static void Count(string handler) => s_calls.AddOrUpdate(handler, 1, static (_, n) => n + 1);

    public static int Of(string handler) => s_calls.TryGetValue(handler, out var n) ? n : 0;
}

public sealed class SecuredStreamHandler : IStreamRequestHandler<SecuredStreamRequest, int>
{
    public IAsyncEnumerable<int> Handle(SecuredStreamRequest request, CancellationToken ct)
    {
        // Not an iterator on purpose: the call itself must not run for a denied request, which an
        // iterator's deferred body would hide.
        StreamCalls.Count(nameof(SecuredStreamHandler));
        return Produce(request.Count, ct);
    }

    private static async IAsyncEnumerable<int> Produce(int count, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return i;
        }
    }
}

public sealed class DeniedStreamHandler : IStreamRequestHandler<DeniedStreamRequest, int>
{
    public IAsyncEnumerable<int> Handle(DeniedStreamRequest request, CancellationToken ct)
    {
        StreamCalls.Count(nameof(DeniedStreamHandler));
        return Empty();
    }

    private static async IAsyncEnumerable<int> Empty()
    {
        await Task.CompletedTask;
        yield return 1;
    }
}

public sealed class OpenStreamHandler : IStreamRequestHandler<OpenStreamRequest, int>
{
    public IAsyncEnumerable<int> Handle(OpenStreamRequest request, CancellationToken ct) => Produce(request.Count);

    private static async IAsyncEnumerable<int> Produce(int count)
    {
        await Task.CompletedTask;
        for (var i = 1; i <= count; i++)
            yield return i;
    }
}

[Collection("non-parallel-authorization")]
public sealed class StreamAuthorizationTests
{
    [Fact]
    public async Task A_denied_stream_yields_nothing_and_never_reaches_the_handler()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider(TestSecurityContexts.With()); // no Admin: the policy denies
        var mediator = sp.GetRequiredService<IMediator>();

        var yielded = 0;
        var ex = await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new SecuredStreamRequest(3), CancellationToken.None))
                yielded++;
        });

        Assert.Equal(0, yielded);
        Assert.Equal(0, StreamCalls.Of(nameof(SecuredStreamHandler)));
        Assert.Equal(AuthorizationFailure.DefaultDenyCode, ex.Failure.Code);
    }

    [Fact]
    public async Task An_allowed_stream_yields_every_item()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new SecuredStreamRequest(3), CancellationToken.None))
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
        Assert.Equal(1, StreamCalls.Of(nameof(SecuredStreamHandler)));
    }

    [Fact]
    public async Task The_denial_surfaces_on_enumeration_not_on_CreateStream()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider(TestSecurityContexts.With());
        var mediator = sp.GetRequiredService<IMediator>();

        // The check is asynchronous and CreateStream is not, so it runs at the first MoveNextAsync.
        var stream = mediator.CreateStream(new SecuredStreamRequest(3), CancellationToken.None);
        Assert.Equal(0, StreamCalls.Of(nameof(SecuredStreamHandler)));

        await using var enumerator = stream.GetAsyncEnumerator(CancellationToken.None);
        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () => await enumerator.MoveNextAsync());
        Assert.Equal(0, StreamCalls.Of(nameof(SecuredStreamHandler)));
    }

    [Fact]
    public async Task Each_scope_is_authorized_with_its_own_identity()
    {
        using var root = BuildPerScopeIdentityProvider();

        Assert.Equal([1, 2], await Drain(root, TestSecurityContexts.With("Admin")));
        await Assert.ThrowsAsync<AuthorizationDeniedException>(() => Drain(root, TestSecurityContexts.With()));
        Assert.Equal([1, 2], await Drain(root, TestSecurityContexts.With("Admin")));

        static async Task<List<int>> Drain(ServiceProvider root, ISecurityContext ctx)
        {
            using var scope = root.CreateScope();
            scope.ServiceProvider.GetRequiredService<TestSecurityContextAccessor>().Current = ctx;
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var items = new List<int>();
            await foreach (var item in mediator.CreateStream(new SecuredStreamRequest(2), CancellationToken.None))
                items.Add(item);
            return items;
        }
    }

    [Fact]
    public async Task The_static_Mediator_denies_a_stream_too()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider(TestSecurityContexts.With());
        _ = sp.GetRequiredService<PipelineBehaviorStateActivation>();

        var yielded = 0;
        await Assert.ThrowsAsync<AuthorizationDeniedException>(async () =>
        {
            await foreach (var _ in Mediator.CreateStream(new DeniedStreamRequest(3), CancellationToken.None))
                yielded++;
        });

        Assert.Equal(0, yielded);
        Assert.Equal(0, StreamCalls.Of(nameof(DeniedStreamHandler)));
    }

    [Fact]
    public async Task The_static_Mediator_streams_when_the_policy_allows()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        _ = sp.GetRequiredService<PipelineBehaviorStateActivation>();

        var items = new List<int>();
        await foreach (var item in Mediator.CreateStream(new SecuredStreamRequest(3), CancellationToken.None))
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task A_stream_without_policies_is_unaffected()
    {
        using var sp = BuildProvider(TestSecurityContexts.With());
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new OpenStreamRequest(3), CancellationToken.None))
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
    }

    [Fact]
    public async Task Cancellation_passed_to_the_enumerator_reaches_the_handler()
    {
        using var sp = BuildProvider(TestSecurityContexts.With("Admin"));
        var mediator = sp.GetRequiredService<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new SecuredStreamRequest(3), CancellationToken.None)
                               .WithCancellation(cts.Token))
            {
            }
        });
    }

    private static ServiceProvider BuildProvider(ISecurityContext ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddSingleton(new InvocationCounter());
        services.AddScoped<ISecurityContextAccessor>(_ => new TestSecurityContextAccessor { Current = ctx });
        AddStreamHandlers(services);
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildPerScopeIdentityProvider()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddSingleton(new InvocationCounter());
        services.AddScoped<TestSecurityContextAccessor>();
        services.AddScoped<ISecurityContextAccessor>(sp => sp.GetRequiredService<TestSecurityContextAccessor>());
        AddStreamHandlers(services);
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }

    private static void AddStreamHandlers(IServiceCollection services)
    {
        services.AddTransient<SecuredStreamHandler>();
        services.AddTransient<DeniedStreamHandler>();
        services.AddTransient<OpenStreamHandler>();
        services.AddTransient<IntegrationTestHandler>();
    }
}
