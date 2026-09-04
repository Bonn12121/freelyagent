using Freely.Perception.Models;
using Freely.Perception.Serialization;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class ObservationTextSerializerTests
{
    [Fact]
    public void IncludesInteractiveElementCoordinatesAndState()
    {
        var observation = new Observation("windows_uia", "application", "Discord", "Settings",
            [new SemanticElement("theme-light", "radio", "Light", null, "Theme choice", true, true, "appearance-panel",
                new Bounds(100, 200, 80, 32), ObservationConfidence.High)],
            new Dictionary<string, string> { ["windowBounds"] = "0,0,1200,900" }, DateTimeOffset.UtcNow);

        var text = ObservationTextSerializer.Serialize(observation);

        Assert.Contains("theme-light | radio | name=Light", text);
        Assert.Contains("selected=true", text);
        Assert.Contains("top_left: theme-light=\"Light\"", text);
        Assert.Contains("description=Theme choice", text);
        Assert.Contains("parent=appearance-panel", text);
        Assert.Contains("bounds=100,200,80,32", text);
    }
}
