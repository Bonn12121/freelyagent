using Freely.AI.Providers;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class ProtocolToolCallParserTests
{
    [Fact]
    public void ParsesToolCallForModelsWithoutNativeTools()
    {
        const string response = "<action>{\"name\":\"perception.read\",\"arguments\":{\"target\":\"active_window\"}}</action>";

        var parsed = ProtocolToolCallParser.TryParse(response, out var call);

        Assert.True(parsed);
        Assert.NotNull(call);
        Assert.Equal("perception.read", call.Name);
        Assert.Contains("active_window", call.ArgumentsJson, StringComparison.Ordinal);
    }
}

