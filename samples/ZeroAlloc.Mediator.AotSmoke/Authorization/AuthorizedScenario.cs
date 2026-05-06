using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.AotSmoke.Internal;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.AotSmoke.Authorization;

#pragma warning disable MA0048
[AuthorizationPolicy("AotAdmin")]
public sealed class AotAdminPolicy : IAuthorizationPolicy
{
    public bool IsAuthorized(ISecurityContext ctx) =>
        ctx.Roles.Contains("Admin");
}

[Authorize("AotAdmin")]
public sealed record AotThrowAllow(int Id) : IRequest<int>;

[Authorize("AotAdmin")]
public sealed record AotThrowDeny(int Id) : IRequest<int>;

[Authorize("AotAdmin")]
public sealed record AotResultAllow(int Id) : IAuthorizedRequest<int>;

public sealed class AotThrowAllowHandler : IRequestHandler<AotThrowAllow, int>
{
    public ValueTask<int> Handle(AotThrowAllow r, CancellationToken ct) => ValueTask.FromResult(r.Id * 2);
}
public sealed class AotThrowDenyHandler : IRequestHandler<AotThrowDeny, int>
{
    public ValueTask<int> Handle(AotThrowDeny r, CancellationToken ct) => ValueTask.FromResult(r.Id * 2);
}
public sealed class AotResultAllowHandler : IRequestHandler<AotResultAllow, Result<int, AuthorizationFailure>>
{
    public ValueTask<Result<int, AuthorizationFailure>> Handle(AotResultAllow r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2);
}

internal sealed record AotCtx(string Id, IReadOnlySet<string> Roles, IReadOnlyDictionary<string, string> Claims) : ISecurityContext;
#pragma warning restore MA0048

internal static class AuthorizedScenario
{
    public static void Run()
    {
        var adminCtx = new AotCtx("alice",
            new HashSet<string>(StringComparer.Ordinal) { "Admin" },
            new Dictionary<string, string>(StringComparer.Ordinal));
        var anonCtx = new AotCtx("anon",
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        VerifyBehavior(adminCtx, anonCtx);
        Console.WriteLine("Mediator.Authorization: throw + Result path OK");

        VerifyAllocationBudget(adminCtx, anonCtx);
        Console.WriteLine("AllocationGate OK");
    }

    private static void VerifyBehavior(AotCtx adminCtx, AotCtx anonCtx)
    {
        // Drive AuthorizationBehavior.Handle directly — bypass the dispatcher to keep the
        // smoke test minimal and AOT-deterministic. The pipeline integration is exercised
        // by the JIT-side AllocationBudgetTests.
        var allowSp = new ServiceCollection();
        allowSp.AddScoped<AotAdminPolicy>();
        allowSp.AddScoped<ISecurityContext>(_ => adminCtx);
        AuthorizationBehaviorState.ServiceProvider = allowSp.BuildServiceProvider();

        var allowResult = AuthorizationBehavior.Handle<AotThrowAllow, int>(
            new AotThrowAllow(7), CancellationToken.None,
            static (r, _) => ValueTask.FromResult(r.Id * 2)).GetAwaiter().GetResult();
        if (allowResult != 14) throw new InvalidOperationException("throw-allow regressed");

        // Throw deny via anonymous context.
        var denySp = new ServiceCollection();
        denySp.AddScoped<AotAdminPolicy>();
        denySp.AddScoped<ISecurityContext>(_ => anonCtx);
        AuthorizationBehaviorState.ServiceProvider = denySp.BuildServiceProvider();
        try
        {
            _ = AuthorizationBehavior.Handle<AotThrowDeny, int>(
                new AotThrowDeny(7), CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)).GetAwaiter().GetResult();
            throw new InvalidOperationException("throw-deny did not throw");
        }
        catch (AuthorizationDeniedException) { /* expected */ }

        // Result path — allow.
        AuthorizationBehaviorState.ServiceProvider = allowSp.BuildServiceProvider();
        var resultAllow = AuthorizationBehavior.Handle<AotResultAllow, Result<int, AuthorizationFailure>>(
            new AotResultAllow(5), CancellationToken.None,
            static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2))
            .GetAwaiter().GetResult();
        if (!resultAllow.IsSuccess || resultAllow.Value != 10)
            throw new InvalidOperationException("result-allow regressed");
    }

    private static void VerifyAllocationBudget(AotCtx adminCtx, AotCtx anonCtx)
    {
        // Budgets reflect the expected per-call overhead of the behavior on the
        // happy path: ValueTask<T> wrapping incurs a small completion-record
        // allocation per await. Adjust upward only on documented evidence.
        var policy = new AotAdminPolicy();

        AllocationGate.AssertBudget(0, 1000,
            () => _ = policy.IsAuthorized(adminCtx),
            "Policy.IsAuthorized (allow)");

        AllocationGate.AssertBudget(0, 1000,
            () => _ = policy.IsAuthorized(anonCtx),
            "Policy.IsAuthorized (deny anonymous)");

        AllocationGate.AssertBudget(0, 1000,
            () => _ = ((IAuthorizationPolicy)policy).Evaluate(adminCtx),
            "Policy.Evaluate (allow)");

        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => ((IAuthorizationPolicy)policy).IsAuthorizedAsync(adminCtx),
            "Policy.IsAuthorizedAsync (allow)");
    }
}
