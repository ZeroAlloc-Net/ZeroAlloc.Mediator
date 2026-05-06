using System.Linq;

namespace ZeroAlloc.Mediator.Authorization.Generator.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void ZAMA001_UnknownPolicy()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [Authorize("DoesNotExist")]
            public sealed record GetThing(int Id) : IRequest<int>;
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA001");
    }

    [Fact]
    public void ZAMA002_DuplicatePolicyName()
    {
        var source = """
            using ZeroAlloc.Authorization;

            [AuthorizationPolicy("Admin")]
            public sealed class FirstAdminPolicy : IAuthorizationPolicy
            {
                public bool IsAuthorized(ISecurityContext ctx) => false;
            }

            [AuthorizationPolicy("Admin")]
            public sealed class SecondAdminPolicy : IAuthorizationPolicy
            {
                public bool IsAuthorized(ISecurityContext ctx) => false;
            }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA002");
    }

    [Fact]
    public void ZAMA003_AuthorizedRequestWithoutAuthorize()
    {
        var source = """
            using ZeroAlloc.Mediator.Authorization;

            public sealed record GetThing(int Id) : IAuthorizedRequest<int>;
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA003");
    }

    [Fact]
    public void ZAMA004_AuthorizeOnNonRequest()
    {
        var source = """
            using ZeroAlloc.Authorization;

            [AuthorizationPolicy("Admin")]
            public sealed class AdminPolicy : IAuthorizationPolicy
            {
                public bool IsAuthorized(ISecurityContext ctx) => false;
            }

            [Authorize("Admin")]
            public sealed class JustAClass { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA004");
    }

    [Fact]
    public void ZAMA005_UnsupportedAttributeProperty()
    {
        // Use a fake "Mode" named arg. The current contract's [Authorize] has only the positional
        // policy name + an inherited TypeId property; setting any *other* named property should
        // fire ZAMA005 (which is the host-version-mismatch signal).
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [AuthorizationPolicy("Admin")]
            public sealed class AdminPolicy : IAuthorizationPolicy
            {
                public bool IsAuthorized(ISecurityContext ctx) => false;
            }

            [Authorize("Admin", Mode = "Any")]
            public sealed record GetThing(int Id) : IRequest<int>;
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA005");
    }

    [Fact]
    public void ZAMA006_AuthorizeOnNotification()
    {
        var source = """
            using ZeroAlloc.Authorization;
            using ZeroAlloc.Mediator;

            [AuthorizationPolicy("Admin")]
            public sealed class AdminPolicy : IAuthorizationPolicy
            {
                public bool IsAuthorized(ISecurityContext ctx) => false;
            }

            [Authorize("Admin")]
            public sealed record SomethingHappened(int Id) : INotification;
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMA006");
    }
}
