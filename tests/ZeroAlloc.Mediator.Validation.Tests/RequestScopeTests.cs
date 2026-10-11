using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Mediator;
using ZeroAlloc.Mediator.Validation;
using ZeroAlloc.Results;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Mediator.Validation.Tests;

// -- Scoped validator fixtures -------------------------------------------------

public readonly record struct TenantRequest(string Name) : IRequest<Result<string, ValidationError>>;

public sealed class TenantRequestHandler : IRequestHandler<TenantRequest, Result<string, ValidationError>>
{
    public ValueTask<Result<string, ValidationError>> Handle(TenantRequest request, CancellationToken ct) =>
        ValueTask.FromResult(Result<string, ValidationError>.Success(request.Name));
}

// Per-request state a validator depends on, such as the current tenant's rules or a DbContext.
public sealed class TenantRules
{
    public string? ForbiddenName { get; set; }
}

// Counts the validators the container constructs, to tell a per-request validator from one
// cached for the container's lifetime.
public sealed class ValidatorConstructions
{
    public int Count;
}

public sealed class TenantRequestValidator : ValidatorFor<TenantRequest>
{
    private readonly TenantRules _rules;

    public TenantRequestValidator(TenantRules rules, ValidatorConstructions constructions)
    {
        _rules = rules;
        Interlocked.Increment(ref constructions.Count);
    }

    public override ValidationResult Validate(TenantRequest instance) =>
        string.Equals(instance.Name, _rules.ForbiddenName, StringComparison.Ordinal)
            ? new ValidationResult([new ValidationFailure { PropertyName = "Name", ErrorMessage = "is forbidden" }])
            : new ValidationResult([]);
}

// -- Tests --------------------------------------------------------------------

// Regression tests for validating each request with the validator of the request being
// dispatched. A scoped validator resolved from the root provider is created once and shared,
// with everything it depends on, by every later request.
[Collection("non-parallel-validation")]
public sealed class RequestScopeTests : IDisposable
{
    public RequestScopeTests() => ValidationBehaviorState.ServiceProvider = null;

    public void Dispose() => ValidationBehaviorState.ServiceProvider = null;

    [Fact]
    public async Task Each_scope_is_validated_with_its_own_scoped_validator()
    {
        using var root = BuildProvider(new ValidatorConstructions());

        Assert.True((await SendInScope(root, forbiddenName: "Alice", name: "Alice")).IsFailure);
        Assert.True((await SendInScope(root, forbiddenName: "Bob", name: "Alice")).IsSuccess);
        Assert.True((await SendInScope(root, forbiddenName: "Bob", name: "Bob")).IsFailure);
    }

    [Fact]
    public async Task Two_containers_in_one_process_validate_against_their_own_services()
    {
        using var validatingHost = BuildProvider(new ValidatorConstructions());
        var otherHost = BuildProvider(new ValidatorConstructions());

        using (var validatingScope = validatingHost.CreateScope())
        using (var otherScope = otherHost.CreateScope())
        {
            validatingScope.ServiceProvider.GetRequiredService<TenantRules>().ForbiddenName = "Alice";
            // Resolving IMediator runs each container's PipelineBehaviorStateActivation, the
            // other host's last.
            var mediator = validatingScope.ServiceProvider.GetRequiredService<IMediator>();
            _ = otherScope.ServiceProvider.GetRequiredService<IMediator>();

            Assert.True((await mediator.Send(new TenantRequest("Alice"))).IsFailure);
        }

        // Disposing the other container must not switch validation off for this one.
        otherHost.Dispose();

        Assert.True((await SendInScope(validatingHost, forbiddenName: "Alice", name: "Alice")).IsFailure);
    }

    [Fact]
    public async Task Static_dispatch_resolves_a_scoped_validator_per_request()
    {
        // The static Mediator class has no request scope, so the behavior opens one per request
        // instead of resolving the scoped validator once from the root.
        var constructions = new ValidatorConstructions();
        using var root = BuildProvider(constructions);
        _ = root.GetRequiredService<PipelineBehaviorStateActivation>();

        Assert.True((await Mediator.Send(new TenantRequest("Alice"))).IsSuccess);
        Assert.True((await Mediator.Send(new TenantRequest("Bob"))).IsSuccess);

        Assert.Equal(2, constructions.Count);
    }

    [Fact]
    public async Task The_handle_without_a_provider_validates_against_PipelineBehaviorServices_Current()
    {
        // IMediator.Send passes its provider to the Handle overload that takes one. The other
        // overload, which older generators call, reads it from PipelineBehaviorServices.Current
        // and must still validate against that scope, not a scope of its own.
        using var root = BuildProvider(new ValidatorConstructions());
        _ = root.GetRequiredService<PipelineBehaviorStateActivation>();
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantRules>().ForbiddenName = "Alice";

        PipelineBehaviorServices.Current = scope.ServiceProvider;
        var result = await ValidationBehavior.Handle<TenantRequest, Result<string, ValidationError>>(
            new TenantRequest("Alice"), CancellationToken.None,
            static (r, _) => ValueTask.FromResult(Result<string, ValidationError>.Success(r.Name)));

        Assert.True(result.IsFailure);
    }

    private static async Task<Result<string, ValidationError>> SendInScope(
        ServiceProvider root, string forbiddenName, string name)
    {
        using var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantRules>().ForbiddenName = forbiddenName;
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(new TenantRequest(name)).ConfigureAwait(false);
    }

    // ValidateScopes stays off, as it is in production outside the Development environment:
    // with it on, the container itself would reject resolving a scoped service from the root.
    private static ServiceProvider BuildProvider(ValidatorConstructions constructions)
    {
        var services = new ServiceCollection();
        services.AddSingleton(constructions);
        services.AddScoped<TenantRules>();
        services.AddScoped<ValidatorFor<TenantRequest>, TenantRequestValidator>();
        services.AddTransient<TenantRequestHandler>();
        services.AddMediator().WithValidation();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
    }
}
