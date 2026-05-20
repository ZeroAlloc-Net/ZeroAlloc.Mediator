using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.AotSmoke.Internal;
using ZeroAlloc.Mediator.Authorization;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.AotSmoke.Authorization;

#pragma warning disable MA0048
[Policy("AotAdmin")]
public sealed class AotAdminPolicy : IAuthorizationPolicy
{
    public ValueTask<UnitResult<AuthorizationFailure>> EvaluateAsync(
        ISecurityContext ctx, CancellationToken ct = default)
        => new(ctx.Roles.Contains("Admin")
            ? UnitResult<AuthorizationFailure>.Success()
            : new AuthorizationFailure(AuthorizationFailure.DefaultDenyCode, "Admin role required"));
}

[RequirePolicy("AotAdmin")]
public sealed record AotThrowAllow(int Id) : IRequest<int>;

[RequirePolicy("AotAdmin")]
public sealed record AotThrowDeny(int Id) : IRequest<int>;

[RequirePolicy("AotAdmin")]
public sealed record AotResultAllow(int Id) : IAuthorizedRequest<int>;

[RequirePolicy("AotAdmin")]
public sealed record AotResultDeny(int Id) : IAuthorizedRequest<int>;

// Stub handlers — required by ZAM001 (every IRequest<T> needs a registered handler in the
// compilation). The scenario drives AuthorizationBehavior.Handle directly, so these are never
// invoked, but their presence keeps the source generator quiet.
public sealed class AotThrowAllowHandler : IRequestHandler<AotThrowAllow, int>
{
    public ValueTask<int> Handle(AotThrowAllow r, CancellationToken ct) => ValueTask.FromResult(r.Id * 2);
}
public sealed class AotThrowDenyHandler : IRequestHandler<AotThrowDeny, int>
{
    public ValueTask<int> Handle(AotThrowDeny r, CancellationToken ct) => ValueTask.FromResult(r.Id);
}
public sealed class AotResultAllowHandler : IRequestHandler<AotResultAllow, Result<int, AuthorizationFailure>>
{
    public ValueTask<Result<int, AuthorizationFailure>> Handle(AotResultAllow r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2);
}
public sealed class AotResultDenyHandler : IRequestHandler<AotResultDeny, Result<int, AuthorizationFailure>>
{
    public ValueTask<Result<int, AuthorizationFailure>> Handle(AotResultDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id);
}

internal sealed record AotCtx(string Id, IReadOnlySet<string> Roles, IReadOnlyDictionary<string, string> Claims) : ISecurityContext;

internal sealed class AotCtxAccessor(ISecurityContext current) : ISecurityContextAccessor
{
    public ISecurityContext Current { get; } = current;
}
#pragma warning restore MA0048

internal static class AuthorizedScenario
{
    // AOT trim preservation: AuthorizationFailureFactory<Result<int, AuthorizationFailure>> reflects on
    // Result<int, AuthorizationFailure>.Failure(AuthorizationFailure) at runtime. Without an explicit
    // static reference the trimmer strips the closed-type method (the handlers only use the implicit
    // int → Result<int,_> Success conversion). [DynamicDependency] forces preservation.
    //
    // Consumers using IAuthorizedRequest<TPayload> in AOT publish must apply the same pattern for their
    // own TPayload — see docs/authorization.md "AOT publish" section. Tracking a library-side fix as
    // a v2.1 enhancement (would require a generator-emitted registration of the closed-type Failure
    // delegate per [RequirePolicy]-decorated IAuthorizedRequest type).
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Result<int, AuthorizationFailure>))]
    public static void Run()
    {
        var adminCtx = new AotCtx("alice",
            new HashSet<string>(StringComparer.Ordinal) { "Admin" },
            new Dictionary<string, string>(StringComparer.Ordinal));
        var anonCtx = new AotCtx("anon",
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        VerifyThrowPath(adminCtx, anonCtx);
        Console.WriteLine("Mediator.Authorization: throw path (IRequest<T>) OK");

        VerifyResultPath(adminCtx, anonCtx);
        Console.WriteLine("Mediator.Authorization: Result path (IAuthorizedRequest<T>) OK");

        VerifyAllocationBudget(adminCtx);
        Console.WriteLine("Mediator.Authorization: AllocationGate OK");

        Console.WriteLine("[Authorization AotSmoke] OK");
    }

    private static void VerifyThrowPath(AotCtx adminCtx, AotCtx anonCtx)
    {
        // Allow — handler runs.
        using (var sp = BuildProvider(adminCtx))
        using (var scope = sp.CreateScope())
        {
            AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
            var allowResult = AuthorizationBehavior.Handle<AotThrowAllow, int>(
                new AotThrowAllow(7), CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)).GetAwaiter().GetResult();
            if (allowResult != 14) throw new InvalidOperationException("throw-allow regressed");
        }

        // Deny — anonymous context → AuthorizationDeniedException.
        using (var sp = BuildProvider(anonCtx))
        using (var scope = sp.CreateScope())
        {
            AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
            try
            {
                _ = AuthorizationBehavior.Handle<AotThrowDeny, int>(
                    new AotThrowDeny(7), CancellationToken.None,
                    static (r, _) => ValueTask.FromResult(r.Id * 2)).GetAwaiter().GetResult();
                throw new InvalidOperationException("throw-deny did not throw");
            }
            catch (AuthorizationDeniedException) { /* expected */ }
        }
    }

    private static void VerifyResultPath(AotCtx adminCtx, AotCtx anonCtx)
    {
        // Allow — Result<T,AuthorizationFailure>.Success.
        using (var sp = BuildProvider(adminCtx))
        using (var scope = sp.CreateScope())
        {
            AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
            var resultAllow = AuthorizationBehavior.Handle<AotResultAllow, Result<int, AuthorizationFailure>>(
                new AotResultAllow(5), CancellationToken.None,
                static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2))
                .GetAwaiter().GetResult();
            if (!resultAllow.IsSuccess || resultAllow.Value != 10)
                throw new InvalidOperationException("result-allow regressed");
        }

        // Deny — Result<T,AuthorizationFailure>.Failure.
        using (var sp = BuildProvider(anonCtx))
        using (var scope = sp.CreateScope())
        {
            AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
            var resultDeny = AuthorizationBehavior.Handle<AotResultDeny, Result<int, AuthorizationFailure>>(
                new AotResultDeny(5), CancellationToken.None,
                static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id))
                .GetAwaiter().GetResult();
            if (resultDeny.IsSuccess)
                throw new InvalidOperationException("result-deny did not return Failure");
            if (!string.Equals(resultDeny.Error.Code, AuthorizationFailure.DefaultDenyCode, StringComparison.Ordinal))
                throw new InvalidOperationException("result-deny code regressed");
        }
    }

    private static void VerifyAllocationBudget(AotCtx adminCtx)
    {
        // Allow happy path through the full AuthorizationBehavior.Handle pipeline. 512 B budget
        // absorbs the Debug-mode async state machine box; Release/AOT path is 0 B/call.
        using var sp = BuildProvider(adminCtx);
        using var scope = sp.CreateScope();
        AuthorizationBehaviorState.ServiceProvider = scope.ServiceProvider;
        var req = new AotThrowAllow(7);

        AllocationGate.AssertBudgetValueTask(512, 1000,
            () => AuthorizationBehavior.Handle<AotThrowAllow, int>(req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)),
            "AuthorizationBehavior.Handle (AOT smoke allow happy path)");

        // Direct policy invocation — tightest budget (0 B/call) on v2's single-method
        // IAuthorizationPolicy.EvaluateAsync returning UnitResult<AuthorizationFailure>.
        IAuthorizationPolicy policy = new AotAdminPolicy();
        AllocationGate.AssertBudgetValueTask(0, 1000,
            () => policy.EvaluateAsync(adminCtx),
            "Policy.EvaluateAsync (AOT smoke allow)");
    }

    private static ServiceProvider BuildProvider(AotCtx ctx)
    {
        var services = new ServiceCollection();
        services.AddZeroAllocAuthorization();
        services.AddScoped<ISecurityContextAccessor>(_ => new AotCtxAccessor(ctx));
        services.AddMediator().WithAuthorization(o => o.UseAccessor<ISecurityContextAccessor>());
        return services.BuildServiceProvider();
    }
}
