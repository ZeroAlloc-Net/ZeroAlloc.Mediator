using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ZMediator.Tests.IntegrationTests;

public readonly record struct IntegrationCountTo(int Max) : IStreamRequest<int>;

public class IntegrationCountToHandler : IStreamRequestHandler<IntegrationCountTo, int>
{
    public async IAsyncEnumerable<int> Handle(
        IntegrationCountTo request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Max; i++)
        {
            yield return i;
        }
    }
}

public class StreamIntegrationTests
{
    [Fact]
    public async Task CreateStream_YieldsExpectedValues()
    {
        var results = new List<int>();

        await foreach (var n in Mediator.CreateStream(new IntegrationCountTo(5), CancellationToken.None))
        {
            results.Add(n);
        }

        Assert.Equal([1, 2, 3, 4, 5], results);
    }
}
