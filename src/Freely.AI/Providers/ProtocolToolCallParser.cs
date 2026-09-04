using System.Text.Json;
using System.Text.RegularExpressions;
using Freely.Agent.Models;

namespace Freely.AI.Providers;

public static partial class ProtocolToolCallParser
{
    public static bool TryParse(string response, out ToolCall? toolCall)
    {
        toolCall = null;
        var match = ActionBlock().Match(response);
        var candidate = match.Success ? match.Groups[1].Value : response.Trim();
        try
        {
            using var document = JsonDocument.Parse(candidate);
            var root = document.RootElement;
            var name = ReadName(root);
            if (string.IsNullOrWhiteSpace(name)) return false;
            var arguments = root.TryGetProperty("arguments", out var value) && value.ValueKind == JsonValueKind.Object
                ? value.GetRawText()
                : "{}";
            toolCall = new ToolCall(Guid.NewGuid().ToString("N"), name, arguments);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string BuildInstruction(IReadOnlyList<ToolDefinition> tools)
    {
        var catalog = string.Join("\n", tools.Select(tool => $"- {tool.Name}: {tool.Description} Parameters: {tool.ParametersJsonSchema}"));
        return "This model is operating in Freely protocol mode because native function calling is unavailable.\n" +
            "Available actions:\n" + catalog + "\n\n" +
            "When an action is required, output only this format, with valid JSON arguments:\n" +
            "<action>{\"name\":\"tool.name\",\"arguments\":{}}</action>\n" +
            "Otherwise answer normally. Never invent an action outside the catalog.";
    }

    private static string? ReadName(JsonElement root)
    {
        foreach (var property in new[] { "name", "tool", "action" })
        {
            if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String) return value.GetString();
        }
        return null;
    }

    [GeneratedRegex("<action>\\s*([\\s\\S]*?)\\s*</action>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ActionBlock();
}
