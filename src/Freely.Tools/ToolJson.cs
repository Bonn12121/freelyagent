using System.Text.Json;

namespace Freely.Tools;

internal static class ToolJson
{
    public static string RequiredString(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"A non-empty '{property}' string is required.");
        }

        return value.GetString()!;
    }
}

