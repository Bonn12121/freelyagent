using Freely.AI.Providers;
using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class SwitchableModelProviderTests
{
    [Fact]
    public async Task SwitchToRoutesTheNextRequestWithoutReplacingTheRouter()
    {
        var router = new SwitchableModelProvider(new StubProvider("first"));
        var request = new AgentRequest("test", [], []);

        Assert.Equal("first", await ReadAsync(router, request));
        router.SwitchTo(new StubProvider("second"));
        Assert.Equal("second", await ReadAsync(router, request));
    }

    private static async Task<string> ReadAsync(IModelProvider provider, AgentRequest request)
    {
        var result = string.Empty;
        await foreach (var chunk in provider.StreamAsync(request, CancellationToken.None)) result += chunk.Text;
        return result;
    }

    private sealed class StubProvider(string value) : IModelProvider
    {
        public string Id => value;
        public string DisplayName => value;

        public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(
            AgentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new ModelStreamChunk(value, IsComplete: true);
        }
    }
}
