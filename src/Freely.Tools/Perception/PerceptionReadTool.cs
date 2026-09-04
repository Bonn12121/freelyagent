using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Freely.Perception;
using Freely.Perception.Models;
using Freely.Perception.Serialization;

namespace Freely.Tools.Perception;

public sealed class PerceptionReadTool(ApplicationPerceptionSession session) : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "perception.read",
        "Read the active Windows application or a file as a normalized semantic observation. Active windows fuse accessibility data with local screenshot OCR, so canvas/custom apps remain readable and text-only models receive the same vision. Use this before acting when the user refers to visible or file content.",
        "{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\",\"enum\":[\"active_window\",\"file\"]},\"path\":{\"type\":\"string\"},\"detail\":{\"type\":\"string\",\"enum\":[\"compact\",\"normal\",\"full\"]}},\"required\":[\"target\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(call.ArgumentsJson);
            var root = document.RootElement;
            var targetName = root.GetProperty("target").GetString();
            var path = root.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
            var detailName = root.TryGetProperty("detail", out var detailValue) ? detailValue.GetString() : "normal";
            var target = targetName == "file"
                ? new PerceptionTarget(PerceptionTargetKind.File, path)
                : new PerceptionTarget(PerceptionTargetKind.ActiveWindow);
            var detail = detailName switch
            {
                "compact" => ObservationDetail.Compact,
                "full" => ObservationDetail.Full,
                _ => ObservationDetail.Normal
            };
            var options = detail switch
            {
                ObservationDetail.Compact => new ObservationOptions(detail, 80, 12_000),
                ObservationDetail.Full => new ObservationOptions(detail, 300, 80_000),
                _ => new ObservationOptions(detail, 180, 24_000)
            };
            var observation = await session.ObserveAsync(target, options, cancellationToken).ConfigureAwait(false);
            return new ToolResult(call.Id, call.Name, true, ObservationTextSerializer.Serialize(observation));
        }
        catch (Exception exception) when (exception is PerceptionUnavailableException or FileNotFoundException or UnauthorizedAccessException or IOException or JsonException or InvalidOperationException)
        {
            return new ToolResult(call.Id, call.Name, false, "", exception.Message);
        }
    }
}
