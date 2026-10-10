using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Authorization;
using ZeroAlloc.Authorization.Generated;
using ZeroAlloc.Mediator;
using ZeroAlloc.TestHelpers;
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

public sealed record AotPayload(int Id);
public readonly record struct AotValuePayload(int Id);

[RequirePolicy("AotAdmin")]
public sealed record AotClassDeny(int Id) : IRequest<Result<AotPayload, AuthorizationFailure>>;

[RequirePolicy("AotAdmin")]
public sealed record AotStructDeny(int Id) : IRequest<Result<AotValuePayload, AuthorizationFailure>>;

[RequirePolicy("AotAdmin")]
public sealed record AotUnitDeny(int Id) : IRequest<UnitResult<AuthorizationFailure>>;

public sealed class AotClassDenyHandler : IRequestHandler<AotClassDeny, Result<AotPayload, AuthorizationFailure>>
{
    public ValueTask<Result<AotPayload, AuthorizationFailure>> Handle(AotClassDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotPayload, AuthorizationFailure>>(new AotPayload(r.Id));
}
public sealed class AotStructDenyHandler : IRequestHandler<AotStructDeny, Result<AotValuePayload, AuthorizationFailure>>
{
    public ValueTask<Result<AotValuePayload, AuthorizationFailure>> Handle(AotStructDeny r, CancellationToken ct)
        => ValueTask.FromResult<Result<AotValuePayload, AuthorizationFailure>>(new AotValuePayload(r.Id));
}
public sealed class AotUnitDenyHandler : IRequestHandler<AotUnitDeny, UnitResult<AuthorizationFailure>>
{
    public ValueTask<UnitResult<AuthorizationFailure>> Handle(AotUnitDeny r, CancellationToken ct)
        => ValueTask.FromResult(UnitResult<AuthorizationFailure>.Success());
}

internal sealed record AotCtx(string Id, IReadOnlySet<string> Roles, IReadOnlyDictionary<string, string> Claims) : ISecurityContext;

internal sealed class AotCtxAccessor(ISecurityContext current) : ISecurityContextAccessor
{
    public ISecurityContext Current { get; } = current;
}
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
            // Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
            // AuthorizationBehaviorState.ServiceProvider as a side effect.
            _ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
            var allowResult = AuthorizationBehavior.Handle<AotThrowAllow, int>(
                new AotThrowAllow(7), CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)).GetAwaiter().GetResult();
            if (allowResult != 14) throw new InvalidOperationException("throw-allow regressed");
        }

        // Deny — anonymous context → AuthorizationDeniedException.
        using (var sp = BuildProvider(anonCtx))
        using (var scope = sp.CreateScope())
        {
            // Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
            // AuthorizationBehaviorState.ServiceProvider as a side effect.
            _ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
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
            // Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
            // AuthorizationBehaviorState.ServiceProvider as a side effect.
            _ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
            var resultAllow = AuthorizationBehavior.Handle<AotResultAllow, Result<int, AuthorizationFailure>>(
                new AotResultAllow(5), CancellationToken.None,
                static (r, _) => ValueTask.FromResult<Result<int, AuthorizationFailure>>(r.Id * 2))
                .GetAwaiter().GetResult();
            if (!resultAllow.IsSuccess || resultAllow.Value != 10)
                throw new InvalidOperationException("result-allow regressed");
        }

        // Deny: a failed result for every response shape (int, class and struct payload, unit).
        VerifyDeny("result-deny", anonCtx, new AotResultDeny(5),
            static (Result<int, AuthorizationFailure> r) => (r.IsSuccess, r.IsSuccess ? null : r.Error.Code));
        VerifyDeny("class-deny", anonCtx, new AotClassDeny(5),
            static (Result<AotPayload, AuthorizationFailure> r) => (r.IsSuccess, r.IsSuccess ? null : r.Error.Code));
        VerifyDeny("struct-deny", anonCtx, new AotStructDeny(5),
            static (Result<AotValuePayload, AuthorizationFailure> r) => (r.IsSuccess, r.IsSuccess ? null : r.Error.Code));
        VerifyDeny("unit-deny", anonCtx, new AotUnitDeny(5),
            static (UnitResult<AuthorizationFailure> r) => (r.IsSuccess, r.IsSuccess ? null : r.Error.Code));
    }

    private static void VerifyDeny<TRequest, TResponse>(
        string name, AotCtx anonCtx, TRequest request, Func<TResponse, (bool IsSuccess, string? Code)> inspect)
        where TRequest : IRequest<TResponse>
    {
        using var sp = BuildProvider(anonCtx);
        using var scope = sp.CreateScope();
        // Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
        // AuthorizationBehaviorState.ServiceProvider as a side effect.
        _ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
        var response = AuthorizationBehavior.Handle<TRequest, TResponse>(
            request, CancellationToken.None,
            static (_, _) => throw new InvalidOperationException("next must not run"))
            .GetAwaiter().GetResult();
        var (isSuccess, code) = inspect(response);
        if (isSuccess)
            throw new InvalidOperationException($"{name} did not return Failure");
        if (!string.Equals(code, AuthorizationFailure.DefaultDenyCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"{name} code regressed");
    }

    private static void VerifyAllocationBudget(AotCtx adminCtx)
    {
        // Allow happy path through the full AuthorizationBehavior.Handle pipeline. 512 B budget
        // absorbs the Debug-mode async state machine box; Release/AOT path is 0 B/call.
        using var sp = BuildProvider(adminCtx);
        using var scope = sp.CreateScope();
        // Resolving AuthorizationBehaviorAccessor triggers its ctor, which sets
        // AuthorizationBehaviorState.ServiceProvider as a side effect.
        _ = scope.ServiceProvider.GetRequiredService<AuthorizationBehaviorAccessor>();
        var req = new AotThrowAllow(7);

        AllocationGate.AssertBudgetValueTask(512, 1000,
            () => AuthorizationBehavior.Handle<AotThrowAllow, int>(req, CancellationToken.None,
                static (r, _) => ValueTask.FromResult(r.Id * 2)),
            "AuthorizationBehavior.Handle (AOT smoke allow happy path)");
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
