using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation.Tests;

// Stream requests with a validator. Pipeline behaviors do not wrap CreateStream, so the generated
// CreateStream calls ValidationBehavior.CheckStream before the handler (#387).
public readonly record struct ValidatedStreamRequest(string Name, int Count) : IStreamRequest<int>;

public readonly record struct UnvalidatedStreamRequest(int Count) : IStreamRequest<int>;

public sealed class ValidatedStreamRequestValidator : ValidatorFor<ValidatedStreamRequest>
{
    public override ValidationResult Validate(ValidatedStreamRequest instance) =>
        string.IsNullOrWhiteSpace(instance.Name)
            ? new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "must not be empty" }])
            : new ValidationResult([]);
}

// How often ValidatedStreamHandler.Handle was called. Tests are non-parallel, so one counter is safe.
internal static class StreamCalls
{
    private static int s_value;

    public static int Value => Volatile.Read(ref s_value);

    public static void Reset() => Volatile.Write(ref s_value, 0);

    public static void Count() => Interlocked.Increment(ref s_value);
}

public sealed class ValidatedStreamHandler : IStreamRequestHandler<ValidatedStreamRequest, int>
{
    public IAsyncEnumerable<int> Handle(ValidatedStreamRequest request, CancellationToken ct)
    {
        // Not an iterator on purpose: the call itself must not run for an invalid request.
        StreamCalls.Count();
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

public sealed class UnvalidatedStreamHandler : IStreamRequestHandler<UnvalidatedStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(UnvalidatedStreamRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        for (var i = 1; i <= request.Count; i++)
            yield return i;
    }
}

[Collection("non-parallel-validation")]
public sealed class StreamValidationTests : IDisposable
{
    public StreamValidationTests() => ValidationBehaviorState.ServiceProvider = null;

    public void Dispose() => ValidationBehaviorState.ServiceProvider = null;

    [Fact]
    public async Task An_invalid_stream_request_yields_nothing_and_never_reaches_the_handler()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var yielded = 0;
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await foreach (var _ in mediator.CreateStream(new ValidatedStreamRequest("", 3), CancellationToken.None).ConfigureAwait(false))
                yielded++;
        });

        Assert.Equal(0, yielded);
        Assert.Equal(0, StreamCalls.Value);
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task A_valid_stream_request_yields_every_item()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new ValidatedStreamRequest("ok", 3), CancellationToken.None))
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
        Assert.Equal(1, StreamCalls.Value);
    }

    [Fact]
    public async Task The_static_Mediator_rejects_an_invalid_stream_request_too()
    {
        StreamCalls.Reset();
        using var sp = BuildProvider();
        _ = sp.GetRequiredService<PipelineBehaviorStateActivation>();

        await Assert.ThrowsAsync<ValidationFailedException>(async () =>
        {
            await foreach (var _ in Mediator.CreateStream(new ValidatedStreamRequest("", 3), CancellationToken.None).ConfigureAwait(false))
            {
            }
        });

        Assert.Equal(0, StreamCalls.Value);
    }

    [Fact]
    public async Task A_stream_request_without_a_validator_is_unaffected()
    {
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new UnvalidatedStreamRequest(3), CancellationToken.None))
            items.Add(item);

        Assert.Equal([1, 2, 3], items);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ValidatorFor<ValidatedStreamRequest>, ValidatedStreamRequestValidator>();
        services.AddTransient<ValidatedStreamHandler>();
        services.AddTransient<UnvalidatedStreamHandler>();
        services.AddMediator().WithValidation();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
    }
}
