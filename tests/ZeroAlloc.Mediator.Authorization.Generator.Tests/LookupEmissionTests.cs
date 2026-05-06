using System.Threading.Tasks;

namespace ZeroAlloc.Mediator.Authorization.Generator.Tests;

public sealed class LookupEmissionTests
{
    [Fact]
    public Task Emits_Lookup_For_SinglePolicy_OnPlainIRequest()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [AuthorizationPolicy("AdminOnly")]
            public sealed class AdminOnlyPolicy
            {
            }

            [Authorize("AdminOnly")]
            public sealed record GetOrderById(int Id) : IRequest<int>;
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Emits_Lookup_For_Stacked_Policies()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [AuthorizationPolicy("Admin")]
            public sealed class AdminPolicy { }

            [AuthorizationPolicy("Premium")]
            public sealed class PremiumPolicy { }

            [Authorize("Admin")]
            [Authorize("Premium")]
            public sealed record DeleteUserCommand(int Id) : IRequest<bool>;
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Emits_Lookup_For_IAuthorizedRequest()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;
            using ZeroAlloc.Mediator.Authorization;

            [AuthorizationPolicy("AdminOnly")]
            public sealed class AdminOnlyPolicy { }

            [Authorize("AdminOnly")]
            public sealed record GetOrderById(int Id) : IAuthorizedRequest<int>;
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Emits_DIExtensions_For_DiscoveredPolicies()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [AuthorizationPolicy("Admin")]
            public sealed class AdminPolicy { }

            [AuthorizationPolicy("Premium")]
            public sealed class PremiumPolicy { }

            [Authorize("Admin")]
            public sealed record DoStuff(int Id) : IRequest<int>;
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Emits_Nothing_For_Compilation_WithoutAnyAuthorize()
    {
        var source = """
            using ZeroAlloc.Mediator;

            public sealed record Ping(int Id) : IRequest<int>;
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
