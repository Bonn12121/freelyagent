using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Freely.Perception.Models;

namespace Freely.Perception.Providers;

public sealed class UIAutomationPerceptionProvider : IPerceptionProvider
{
    public int Priority => 100;
    public bool CanHandle(PerceptionTarget target) => target.Kind == PerceptionTargetKind.ActiveWindow;

    public Task<Observation> ObserveAsync(PerceptionTarget target, ObservationOptions options, CancellationToken cancellationToken) =>
        Task.Run(() => ObserveActiveWindow(options, cancellationToken), cancellationToken);

    private static Observation ObserveActiveWindow(ObservationOptions options, CancellationToken cancellationToken)
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero) throw new PerceptionUnavailableException("Windows did not report an active window.");
        var root = AutomationElement.FromHandle(handle);
        var rootInfo = root.Current;
        var elements = new List<SemanticElement>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = new StringBuilder();
        var queue = new Queue<(AutomationElement Element, string? ParentId)>();
        var walker = TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(root);
        while (child is not null)
        {
            queue.Enqueue((child, null));
            child = walker.GetNextSibling(child);
        }

        while (queue.Count > 0 && elements.Count < options.MaxElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, parentId) = queue.Dequeue();
            try
            {
                var current = element.Current;
                var type = NormalizeType(current.ControlType.ProgrammaticName);
                var name = current.Name?.Trim() ?? string.Empty;
                var id = MakeUniqueId(BuildId(current.AutomationId, type, name, elements.Count + 1), ids);
                var isPassword = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is true;
                var value = isPassword ? "<REDACTED_PASSWORD>" : ReadValue(element, options.MaxTextCharacters);
                var rectangle = current.BoundingRectangle;
                Bounds? bounds = rectangle.IsEmpty ? null : new Bounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                var isVisible = !current.IsOffscreen && (bounds is null || Intersects(rectangle, rootInfo.BoundingRectangle));

                if (isVisible && (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(value)))
                {
                    elements.Add(new SemanticElement(id, type, name, value, current.HelpText, current.IsEnabled,
                        ReadSelected(element), parentId, bounds, ObservationConfidence.High));
                    if (text.Length < options.MaxTextCharacters)
                    {
                        text.Append('[').Append(type).Append(':').Append(id).Append("] ").Append(name);
                        if (!string.IsNullOrWhiteSpace(value) && value != name) text.Append(": ").Append(value);
                        text.AppendLine();
                    }
                }

                var nested = walker.GetFirstChild(element);
                while (nested is not null && queue.Count + elements.Count < options.MaxElements * 3)
                {
                    queue.Enqueue((nested, id));
                    nested = walker.GetNextSibling(nested);
                }
            }
            catch (ElementNotAvailableException)
            {
                // The UI changed while being read; skip this stale element.
            }
        }

        var metadata = new Dictionary<string, string>
        {
            ["processId"] = rootInfo.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["automationId"] = rootInfo.AutomationId ?? string.Empty,
            ["framework"] = rootInfo.FrameworkId ?? string.Empty,
            ["elementCount"] = elements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["windowBounds"] = FormatBounds(rootInfo.BoundingRectangle)
        };
        var title = string.IsNullOrWhiteSpace(rootInfo.Name) ? "Active window" : rootInfo.Name;
        return new Observation("windows_uia", "application", title, text.ToString().Trim(), elements, metadata,
            DateTimeOffset.UtcNow, elements.Count > 0 || !string.IsNullOrWhiteSpace(title));
    }

    private static string FormatBounds(System.Windows.Rect rectangle) => string.Join(',',
        Math.Round(rectangle.X), Math.Round(rectangle.Y), Math.Round(rectangle.Width), Math.Round(rectangle.Height));

    private static string? ReadValue(AutomationElement element, int maxCharacters)
    {
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern) && valuePattern is ValuePattern value)
        {
            return value.Current.Value;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern) && textPattern is TextPattern text)
        {
            return text.DocumentRange.GetText(Math.Min(maxCharacters, 8_000)).Trim();
        }

        return null;
    }

    private static bool Intersects(System.Windows.Rect first, System.Windows.Rect second) =>
        first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

    private static bool? ReadSelected(AutomationElement element) =>
        element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && pattern is SelectionItemPattern item
            ? item.Current.IsSelected
            : null;

    private static string NormalizeType(string programmaticName) => programmaticName.Replace("ControlType.", string.Empty).ToLowerInvariant() switch
    {
        "edit" => "input",
        "hyperlink" => "link",
        "menuitem" => "menu_item",
        "listitem" => "list_item",
        "radiobutton" => "radio",
        "checkbox" => "checkbox",
        "custom" => "element",
        var value => value
    };

    private static string BuildId(string automationId, string type, string name, int index)
    {
        if (!string.IsNullOrWhiteSpace(automationId)) return Clean(automationId);
        var readable = Clean(name);
        return string.IsNullOrWhiteSpace(readable) ? $"{type}_{index}" : $"{type}_{readable}_{index}";
    }

    private static string MakeUniqueId(string candidate, HashSet<string> ids)
    {
        if (ids.Add(candidate)) return candidate;
        for (var suffix = 2; ; suffix++)
        {
            var unique = $"{candidate}_{suffix}";
            if (ids.Add(unique)) return unique;
        }
    }

    private static string Clean(string value)
    {
        var cleaned = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').ToArray());
        return cleaned.Length > 48 ? cleaned[..48] : cleaned;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
