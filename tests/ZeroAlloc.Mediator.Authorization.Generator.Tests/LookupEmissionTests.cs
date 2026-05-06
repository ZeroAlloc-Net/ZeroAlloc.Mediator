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
}
