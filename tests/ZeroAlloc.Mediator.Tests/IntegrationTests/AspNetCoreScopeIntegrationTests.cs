extern alias aspnetsample;

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;

using SampleProgram = aspnetsample::Program;

namespace ZeroAlloc.Mediator.Tests.IntegrationTests;

public class AspNetCoreScopeIntegrationTests : IClassFixture<WebApplicationFactory<SampleProgram>>
{
    private readonly WebApplicationFactory<SampleProgram> _factory;

    public AspNetCoreScopeIntegrationTests(WebApplicationFactory<SampleProgram> factory)
        => _factory = factory;

    [Fact]
    public async Task Handler_ResolvesScopedDependencyFromRequestScope()
    {
        var client = _factory.CreateClient();

        var first = await client.GetStringAsync("/who");
        var second = await client.GetStringAsync("/who");

        // Each HTTP request gets its own DI scope, so each request's RequestContext
        // (and hence Id) is fresh — the two responses must differ. This is the
        // load-bearing 3.1.0 contract: scoped services flow from the request scope
        // into mediator handlers via IServiceProvider, not via singleton resolution.
        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.NotEqual(first, second);
    }
}
