using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Freely.Computer;
using Freely.Perception.Serialization;
using Freely.Tools.Perception;

namespace Freely.Tools.Computer;

public sealed class AppClickElementTool(
    ComputerControlCoordinator coordinator,
    ApplicationPerceptionSession perception) : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "app.click_element",
        "Click an exact named native-app element from the latest perception.read result. Prefer this over coordinate clicks for contacts, conversations, buttons, tabs, and visually similar controls. It refreshes the app first and returns the resulting observation.",
        "{\"type\":\"object\",\"properties\":{\"elementId\":{\"type\":\"string\"}},\"required\":[\"elementId\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.Write;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var elementId = ToolJson.RequiredString(call.ArgumentsJson, "elementId");
            var (target, _) = await perception.ResolveFreshAsync(elementId, cancellationToken).ConfigureAwait(false);
            coordinator.SetAccessScope(ControlAccessScope.Application);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusTargetWindow();
                await Task.Delay(100, token).ConfigureAwait(false);
                var (x, y) = Center(target);
                await NativeInput.MovePointerHumanAsync(x, y, token).ConfigureAwait(false);
                NativeInput.Click("left", 1);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
            var observation = await perception.ObserveAsync(
                new Freely.Perception.Models.PerceptionTarget(Freely.Perception.Models.PerceptionTargetKind.ActiveWindow),
                new Freely.Perception.Models.ObservationOptions(Freely.Perception.Models.ObservationDetail.Compact, 180, 24_000),
                cancellationToken).ConfigureAwait(false);
            return new ToolResult(call.Id, call.Name, true,
                $"Clicked app element '{target.Name}' ({target.Id}). The latest screen state is included below; use it directly.\n{ObservationTextSerializer.Serialize(observation)}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or
            System.Runtime.InteropServices.ExternalException)
        {
            return new ToolResult(call.Id, call.Name, false, "", exception.Message);
        }
    }

    private static (int X, int Y) Center(ApplicationElementTarget target) =>
        ((int)Math.Round(target.Bounds.X + (target.Bounds.Width / 2)),
            (int)Math.Round(target.Bounds.Y + (target.Bounds.Height / 2)));
}

public sealed class AppTypeElementTool(
    ComputerControlCoordinator coordinator,
    ApplicationPerceptionSession perception) : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "app.type_element",
        "Click an exact native-app input from perception.read, type text with physical Windows input, optionally replace its existing value, and optionally press Enter. Before messaging, verify the intended conversation header is open and select its message composer—not any search field.",
        "{\"type\":\"object\",\"properties\":{\"elementId\":{\"type\":\"string\"},\"text\":{\"type\":\"string\",\"maxLength\":10000},\"replace\":{\"type\":\"boolean\"},\"submit\":{\"type\":\"boolean\"}},\"required\":[\"elementId\",\"text\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.Write;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(call.ArgumentsJson);
            var root = document.RootElement;
            var elementId = root.GetProperty("elementId").GetString() ?? throw new ArgumentException("elementId is required.");
            var text = root.GetProperty("text").GetString() ?? string.Empty;
            if (text.Length > 10_000) throw new ArgumentException("Typed text exceeds the 10,000 character limit.");
            var replace = root.TryGetProperty("replace", out var replaceValue) && replaceValue.GetBoolean();
            var submit = root.TryGetProperty("submit", out var submitValue) && submitValue.GetBoolean();
            var (target, _) = await perception.ResolveFreshAsync(elementId, cancellationToken).ConfigureAwait(false);
            if (!IsEditable(target))
            {
                throw new InvalidOperationException(
                    $"App element '{target.Name}' is a {target.Type}, not a confirmed editable input. Read the app again and choose its textbox or composer.");
            }

            coordinator.SetAccessScope(ControlAccessScope.Application);
            await coordinator.RunExclusiveAsync(async token =>
            {
                coordinator.FocusTargetWindow();
                await Task.Delay(100, token).ConfigureAwait(false);
                var (x, y) = Center(target);
                await NativeInput.MovePointerHumanAsync(x, y, token).ConfigureAwait(false);
                NativeInput.Click("left", 1);
                await Task.Delay(100, token).ConfigureAwait(false);
                if (replace) NativeInput.PressChord("ctrl+a");
                await NativeInput.TypeTextHumanAsync(text, token).ConfigureAwait(false);
                if (submit) NativeInput.PressChord("enter");
                return true;
            }, cancellationToken).ConfigureAwait(false);
            await Task.Delay(submit ? 250 : 120, cancellationToken).ConfigureAwait(false);
            var observation = await perception.ObserveAsync(
                new Freely.Perception.Models.PerceptionTarget(Freely.Perception.Models.PerceptionTargetKind.ActiveWindow),
                new Freely.Perception.Models.ObservationOptions(Freely.Perception.Models.ObservationDetail.Compact, 180, 24_000),
                cancellationToken).ConfigureAwait(false);
            return new ToolResult(call.Id, call.Name, true,
                $"Typed into '{target.Name}' ({target.Id}){(submit ? " and pressed Enter" : string.Empty)}. The latest screen state is included below; use it directly.\n{ObservationTextSerializer.Serialize(observation)}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or
            System.Runtime.InteropServices.ExternalException)
        {
            return new ToolResult(call.Id, call.Name, false, "", exception.Message);
        }
    }

    private static bool IsEditable(ApplicationElementTarget target)
    {
        if (target.Type is "input" or "edit" or "textbox" or "document" or "combobox") return true;
        var combined = $"{target.Name} {target.Description}";
        return combined.Contains("message", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("composer", StringComparison.OrdinalIgnoreCase);
    }

    private static (int X, int Y) Center(ApplicationElementTarget target) =>
        ((int)Math.Round(target.Bounds.X + (target.Bounds.Width / 2)),
            (int)Math.Round(target.Bounds.Y + (target.Bounds.Height / 2)));
}
