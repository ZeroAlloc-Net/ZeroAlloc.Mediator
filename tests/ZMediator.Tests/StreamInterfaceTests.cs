namespace ZMediator.Tests;

public class StreamInterfaceTests
{
    private readonly record struct CountRequest(int Count) : IStreamRequest<int>;

    private class CountHandler : IStreamRequestHandler<CountRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CountRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < request.Count; i++)
            {
                yield return i;
            }
        }
    }

    [Fact]
    public async Task StreamHandler_YieldsExpectedValues()
    {
        var handler = new CountHandler();
        var results = new List<int>();
        await foreach (var item in handler.Handle(new CountRequest(3), CancellationToken.None))
        {
            results.Add(item);
        }
        Assert.Equal([0, 1, 2], results);
    }
}
