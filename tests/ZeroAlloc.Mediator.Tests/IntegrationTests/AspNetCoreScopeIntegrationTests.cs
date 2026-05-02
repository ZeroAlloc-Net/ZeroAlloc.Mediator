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

    [Fact]
    public async Task Multiple_Sends_WithinOneRequest_ShareTheRequestScope()
    {
        var client = _factory.CreateClient();
        var raw = await client.GetStringAsync("/who-twice");

        var ids = raw.Split(',');
        Assert.Equal(2, ids.Length);
        Assert.NotEmpty(ids[0]);
        // Both Send() calls in one HTTP request must hit the same RequestContext (Scoped),
        // so the two Guids must be identical. Would fail if MediatorService ever started
        // creating a fresh sub-scope per Send().
        Assert.Equal(ids[0], ids[1]);
    }
}
