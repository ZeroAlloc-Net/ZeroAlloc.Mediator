using System.Linq;
using ZeroAlloc.Authorization;
using ZeroAlloc.Mediator;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mediator.Authorization.Tests;

public class IAuthorizedRequestTests
{
    [Fact]
    public void IAuthorizedRequest_Extends_IRequest_Of_ResultWrappedResponse()
    {
        var iface = typeof(IAuthorizedRequest<int>);
        Assert.True(iface.IsInterface);

        var implementsRequest = iface.GetInterfaces()
            .Any(t => t.IsGenericType
                   && t.GetGenericTypeDefinition() == typeof(IRequest<>)
                   && t.GenericTypeArguments[0].IsGenericType
                   && t.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(Result<,>)
                   && t.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(int)
                   && t.GenericTypeArguments[0].GenericTypeArguments[1] == typeof(AuthorizationFailure));
        Assert.True(implementsRequest, "IAuthorizedRequest<T> must extend IRequest<Result<T, AuthorizationFailure>>");
    }
}
