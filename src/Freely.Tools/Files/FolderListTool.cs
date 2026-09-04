using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Tools.Files;

public sealed class FolderListTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "folder.list",
        "List the immediate contents of a folder without modifying it.",
        "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(ToolJson.RequiredString(call.ArgumentsJson, "path"));
            if (!Directory.Exists(path)) return Task.FromResult(new ToolResult(call.Id, call.Name, false, "", "Folder not found."));
            var entries = Directory.EnumerateFileSystemEntries(path)
                .Take(250)
                .Select(entry => Directory.Exists(entry) ? $"[Folder] {Path.GetFileName(entry)}" : $"[File] {Path.GetFileName(entry)}");
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, string.Join(Environment.NewLine, entries)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {
            return Task.FromResult(new ToolResult(call.Id, call.Name, false, "", exception.Message));
        }
    }
}

