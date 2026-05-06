using ZeroAlloc.Authorization;

namespace ZeroAlloc.Mediator.Authorization.Tests;

public class AuthorizationDeniedExceptionTests
{
    [Fact]
    public void Exception_CarriesFailureWithCode()
    {
        var failure = new AuthorizationFailure("policy.deny.role.admin", "user lacks admin role");
        var ex = new AuthorizationDeniedException(failure);
        Assert.Equal(failure, ex.Failure);
        Assert.Contains("policy.deny.role.admin", ex.Message, System.StringComparison.Ordinal);
    }
}
