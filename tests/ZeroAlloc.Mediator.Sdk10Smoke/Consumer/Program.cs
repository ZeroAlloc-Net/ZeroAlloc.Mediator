using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Sdk10Smoke;

// The static Mediator class exists only if the compiler loaded the generator, and the behavior
// runs only if the bundled ZeroAlloc.Pipeline.Generators found it.
var pong = await Mediator.Send(new Ping("hello"), CancellationToken.None);
if (pong != "pong: hello" || !CountingBehavior.Ran)
{
    Console.Error.WriteLine($"FAIL: Send returned '{pong}', behavior ran: {CountingBehavior.Ran}");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, request dispatched through the pipeline.");
return 0;

namespace ZeroAlloc.Mediator.Sdk10Smoke
{
    public readonly record struct Ping(string Message) : IRequest<string>;

    public sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public ValueTask<string> Handle(Ping request, CancellationToken ct)
            => ValueTask.FromResult($"pong: {request.Message}");
    }

    [PipelineBehavior(Order = 0)]
    public sealed class CountingBehavior : IPipelineBehavior
    {
        public static bool Ran { get; private set; }

        public static ValueTask<TResponse> Handle<TRequest, TResponse>(
            TRequest request, CancellationToken ct,
            Func<TRequest, CancellationToken, ValueTask<TResponse>> next)
            where TRequest : IRequest<TResponse>
        {
            Ran = true;
            return next(request, ct);
        }
    }
}
