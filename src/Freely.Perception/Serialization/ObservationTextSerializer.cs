using System.Text;
using Freely.Perception.Models;

namespace Freely.Perception.Serialization;

public static class ObservationTextSerializer
{
    public static string Serialize(Observation observation)
    {
        var output = new StringBuilder();
        output.Append("<observation source=\"").Append(observation.Source).Append("\" type=\"")
            .Append(observation.Type).AppendLine("\">");
        output.Append("TITLE: ").AppendLine(observation.Title);
        foreach (var item in observation.Metadata) output.Append(item.Key).Append(": ").AppendLine(item.Value);
        if (!string.IsNullOrWhiteSpace(observation.PlainText))
        {
            output.AppendLine().AppendLine("CONTENT:").AppendLine(observation.PlainText);
        }
        if (observation.Elements.Count > 0)
        {
            AppendSpatialSummary(output, observation);
            output.AppendLine().AppendLine("VISIBLE ELEMENTS AND CONTROLS:");
            foreach (var element in observation.Elements)
            {
                output.Append(element.Id).Append(" | ").Append(element.Type);
                AppendField(output, "name", element.Name);
                AppendField(output, "value", element.Value);
                AppendField(output, "description", element.Description);
                AppendField(output, "parent", element.ParentId);
                if (element.Enabled is not null) output.Append(" | enabled=").Append(element.Enabled.Value ? "true" : "false");
                if (element.Selected is not null) output.Append(" | selected=").Append(element.Selected.Value ? "true" : "false");
                output.Append(" | confidence=").Append(element.Confidence.ToString().ToLowerInvariant());
                if (element.Bounds is { } bounds)
                {
                    output.Append(" | bounds=")
                        .Append(Math.Round(bounds.X)).Append(',').Append(Math.Round(bounds.Y)).Append(',')
                        .Append(Math.Round(bounds.Width)).Append(',').Append(Math.Round(bounds.Height));
                }
                output.AppendLine();
            }
        }
        output.AppendLine("</observation>");
        return output.ToString();
    }

    private static void AppendSpatialSummary(StringBuilder output, Observation observation)
    {
        if (!observation.Metadata.TryGetValue("windowBounds", out var rawBounds)) return;
        var parts = rawBounds.Split(',');
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
            !double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var height) ||
            width <= 0 || height <= 0) return;

        var regions = observation.Elements.Where(item => item.Bounds is not null && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => Region(item.Bounds!, x, y, width, height))
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        output.AppendLine().AppendLine("VISIBLE LAYOUT BY REGION:");
        foreach (var region in regions)
        {
            var items = region.Take(24).Select(item => $"{item.Id}=\"{Compact(item.Name, 70)}\"");
            output.Append(region.Key).Append(": ").AppendLine(string.Join("; ", items));
        }
    }

    private static string Region(Bounds bounds, double x, double y, double width, double height)
    {
        var centerX = (bounds.X + (bounds.Width / 2) - x) / width;
        var centerY = (bounds.Y + (bounds.Height / 2) - y) / height;
        var horizontal = centerX < 0.33 ? "left" : centerX > 0.67 ? "right" : "center";
        var vertical = centerY < 0.33 ? "top" : centerY > 0.67 ? "bottom" : "middle";
        return $"{vertical}_{horizontal}";
    }

    private static string Compact(string value, int maxLength)
    {
        var compact = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length > maxLength ? compact[..maxLength] + "…" : compact;
    }

    private static void AppendField(StringBuilder output, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var compact = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (compact.Length > 240) compact = compact[..240] + "…";
        output.Append(" | ").Append(label).Append('=').Append(compact);
    }
}
