using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Tools.Files;

public sealed class FileReadTool : IAgentTool
{
    private const int MaxCharacters = 100_000;

    public ToolDefinition Definition { get; } = new(
        "file.read",
        "Read a UTF-8 text file. User confirmation is required unless policy explicitly allows it.",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.GetFullPath(ToolJson.RequiredString(call.ArgumentsJson, "path"));
            if (!File.Exists(path)) return new(call.Id, call.Name, false, "", "File not found.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, true);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxCharacters];
            var count = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            var suffix = reader.EndOfStream ? string.Empty : "\n[Output truncated]";
            return new(call.Id, call.Name, true, new string(buffer, 0, count) + suffix);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            return new(call.Id, call.Name, false, "", exception.Message);
        }
    }
}

