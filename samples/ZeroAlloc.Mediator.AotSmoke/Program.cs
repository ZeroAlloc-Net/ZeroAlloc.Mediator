using System;
using System.Threading;
using System.Threading.Tasks;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.AotSmoke;

// Minimal AOT smoke covering the three dispatch shapes the generator emits:
//   * Mediator.Send              — request/response
//   * Mediator.Publish           — notification
//   * Mediator.CreateStream      — streaming

var pong = await Mediator.Send(new Ping("ok"), CancellationToken.None).ConfigureAwait(false);
if (!string.Equals(pong, "Pong: ok", StringComparison.Ordinal))
    return Fail($"Send<Ping,string> expected 'Pong: ok', got '{pong}'");

UserCreatedHandler.Seen = 0;
await Mediator.Publish(new UserCreated(1), CancellationToken.None).ConfigureAwait(false);
if (UserCreatedHandler.Seen != 1)
    return Fail($"Publish<UserCreated> expected 1 handler invocation, got {UserCreatedHandler.Seen}");

var total = 0;
await foreach (var n in Mediator.CreateStream(new CountTo(3), CancellationToken.None).ConfigureAwait(false))
{
    total += n;
}
if (total != 6)  // 1+2+3
    return Fail($"CreateStream<CountTo,int> expected total=6, got {total}");

Console.WriteLine("AOT smoke: PASS");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}

namespace ZeroAlloc.Mediator.AotSmoke
{
    using System.Runtime.CompilerServices;

#pragma warning disable MA0048
    public readonly record struct Ping(string Message) : IRequest<string>;
    public readonly record struct UserCreated(int Id) : INotification;
    public readonly record struct CountTo(int Max) : IStreamRequest<int>;
#pragma warning restore MA0048

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public ValueTask<string> Handle(Ping request, CancellationToken ct)
            => ValueTask.FromResult($"Pong: {request.Message}");
    }

    public sealed class UserCreatedHandler : INotificationHandler<UserCreated>
    {
        public static int Seen;

        public ValueTask Handle(UserCreated notification, CancellationToken ct)
        {
            Interlocked.Increment(ref Seen);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class CountToHandler : IStreamRequestHandler<CountTo, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountTo request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= request.Max; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }
    }
}
