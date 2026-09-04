using Freely.Perception;
using Freely.Perception.Models;
using Xunit;

namespace Freely.Agent.Tests;

public sealed class PerceptionManagerTests
{
    [Fact]
    public async Task FusionReservesSpaceForVisibleOcrWhenAccessibilityTreeIsLarge()
    {
        var structural = Enumerable.Range(1, 100)
            .Select(index => Element($"uia_{index}", "button", $"Control {index}", index))
            .ToArray();
        var visual = Enumerable.Range(1, 50)
            .Select(index => Element($"visual_text_{index}", "text", $"Visible row {index}", index + 200))
            .ToArray();
        var manager = new PerceptionManager([
            new StubProvider(100, Observation("windows_uia", structural)),
            new StubProvider(50, Observation("screen_ocr", visual))
        ]);

        var result = await manager.ObserveAsync(
            new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
            new ObservationOptions(ObservationDetail.Full, 60, 20_000));

        Assert.Equal(60, result.Elements.Count);
        Assert.True(result.Elements.Count(item => item.Id.StartsWith("visual_text_", StringComparison.Ordinal)) >= 20);
        Assert.Contains("Visible row", result.PlainText);
    }

    [Fact]
    public async Task RichAccessibilityTreeSkipsSlowVisualFallbackAtNormalDetail()
    {
        var structural = Enumerable.Range(1, 40)
            .Select(index => Element($"uia_{index}", "button", $"Control {index}", index))
            .ToArray();
        var visualProvider = new CountingProvider(50, Observation("screen_ocr", []));
        var manager = new PerceptionManager([
            new StubProvider(100, Observation("windows_uia", structural)),
            visualProvider
        ]);

        var result = await manager.ObserveAsync(
            new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
            new ObservationOptions(ObservationDetail.Normal, 60, 20_000));

        Assert.Equal("windows_uia", result.Source);
        Assert.Equal(0, visualProvider.CallCount);
    }

    [Fact]
    public async Task ModerateActionableTreeAlsoSkipsSlowVisualFallback()
    {
        var structural = Enumerable.Range(1, 14)
            .Select(index => Element($"uia_{index}", index <= 3 ? "button" : "text", $"Control {index}", index))
            .ToArray();
        var visualProvider = new CountingProvider(50, Observation("screen_ocr", []));
        var manager = new PerceptionManager([
            new StubProvider(100, Observation("windows_uia", structural)),
            visualProvider
        ]);

        var result = await manager.ObserveAsync(
            new PerceptionTarget(PerceptionTargetKind.ActiveWindow),
            new ObservationOptions(ObservationDetail.Normal, 60, 20_000));

        Assert.Equal("windows_uia", result.Source);
        Assert.Equal(0, visualProvider.CallCount);
    }

    private static Observation Observation(string source, IReadOnlyList<SemanticElement> elements) =>
        new(source, "application", "Test", string.Empty, elements,
            new Dictionary<string, string> { ["windowBounds"] = "0,0,1200,900" }, DateTimeOffset.UtcNow);

    private static SemanticElement Element(string id, string type, string name, int y) =>
        new(id, type, name, null, null, true, null, null,
            new Bounds(20, y, 160, 24), ObservationConfidence.High);

    private sealed class StubProvider(int priority, Observation observation) : IPerceptionProvider
    {
        public int Priority => priority;
        public bool CanHandle(PerceptionTarget target) => true;
        public Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken) =>
            Task.FromResult(observation);
    }

    private sealed class CountingProvider(int priority, Observation observation) : IPerceptionProvider
    {
        public int CallCount { get; private set; }
        public int Priority => priority;
        public bool CanHandle(PerceptionTarget target) => true;
        public Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(observation);
        }
    }
}
