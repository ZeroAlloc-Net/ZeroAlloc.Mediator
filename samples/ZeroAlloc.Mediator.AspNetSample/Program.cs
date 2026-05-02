using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.AspNetSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddMediator()
    .RegisterHandlersFromAssembly(typeof(Program).Assembly);

var app = builder.Build();

app.MapGet("/who", async (IMediator mediator, CancellationToken ct) =>
    (await mediator.Send(new GetRequestId(), ct).ConfigureAwait(false)).ToString());

app.Run();

// Required for WebApplicationFactory<Program> to find the entry-point Program type.
public partial class Program { }

namespace ZeroAlloc.Mediator.AspNetSample
{
    public interface IRequestContext
    {
        Guid Id { get; }
    }

    public sealed class RequestContext : IRequestContext
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public readonly record struct GetRequestId : IRequest<Guid>;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "ZeroAlloc.Mediator",
        "ZAM008",
        Justification = "Handler is resolved through DI (services.AddMediator().RegisterHandlersFromAssembly), " +
                        "not via the static Mediator.Send/Publish/CreateStream entry points.")]
    public sealed class GetRequestIdHandler : IRequestHandler<GetRequestId, Guid>
    {
        private readonly IRequestContext _ctx;
        public GetRequestIdHandler(IRequestContext ctx) => _ctx = ctx;
        public ValueTask<Guid> Handle(GetRequestId request, CancellationToken ct)
            => ValueTask.FromResult(_ctx.Id);
    }
}
