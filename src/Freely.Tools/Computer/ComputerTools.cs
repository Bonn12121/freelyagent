using System.Diagnostics;
using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;
using Freely.Computer;

namespace Freely.Tools.Computer;

public sealed class MouseMoveTool(ComputerControlCoordinator coordinator) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("computer.mouse_move", "Move the user's mouse pointer to absolute screen coordinates.",
        "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"}},\"required\":[\"x\",\"y\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => coordinator.RunExclusiveAsync(async token =>
    {
        coordinator.SetAccessScope(ControlAccessScope.Computer);
        using var json = JsonDocument.Parse(call.ArgumentsJson);
        var x = json.RootElement.GetProperty("x").GetInt32();
        var y = json.RootElement.GetProperty("y").GetInt32();
        token.ThrowIfCancellationRequested();
        coordinator.FocusTargetWindow();
        await Task.Delay(100, token);
        NativeInput.MovePointer(x, y);
        return new ToolResult(call.Id, call.Name, true, $"Moved pointer to ({x}, {y}) in the controlled app.");
    }, cancellationToken);
}

public sealed class MouseClickTool(ComputerControlCoordinator coordinator) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("computer.mouse_click", "Click the user's mouse at absolute screen coordinates. Prefer semantic browser or UI Automation actions when available.",
        "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"},\"y\":{\"type\":\"integer\"},\"button\":{\"type\":\"string\",\"enum\":[\"left\",\"right\",\"middle\"]},\"clicks\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":3}},\"required\":[\"x\",\"y\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => coordinator.RunExclusiveAsync(async token =>
    {
        coordinator.SetAccessScope(ControlAccessScope.Computer);
        using var json = JsonDocument.Parse(call.ArgumentsJson);
        var root = json.RootElement;
        var x = root.GetProperty("x").GetInt32();
        var y = root.GetProperty("y").GetInt32();
        var button = root.TryGetProperty("button", out var buttonValue) ? buttonValue.GetString() ?? "left" : "left";
        var clicks = root.TryGetProperty("clicks", out var clickValue) ? clickValue.GetInt32() : 1;
        token.ThrowIfCancellationRequested();
        coordinator.FocusTargetWindow();
        await Task.Delay(100, token);
        NativeInput.MovePointer(x, y);
        NativeInput.Click(button, clicks);
        return new ToolResult(call.Id, call.Name, true, $"Clicked {button} {Math.Clamp(clicks, 1, 3)} time(s) at ({x}, {y}) in the controlled app. Verify with perception.read.");
    }, cancellationToken);
}

public sealed class MouseScrollTool(ComputerControlCoordinator coordinator) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("computer.mouse_scroll", "Scroll the active application. Positive delta scrolls up; negative scrolls down.",
        "{\"type\":\"object\",\"properties\":{\"delta\":{\"type\":\"integer\",\"minimum\":-2400,\"maximum\":2400}},\"required\":[\"delta\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => coordinator.RunExclusiveAsync(async token =>
    {
        coordinator.SetAccessScope(ControlAccessScope.Computer);
        using var json = JsonDocument.Parse(call.ArgumentsJson);
        var delta = Math.Clamp(json.RootElement.GetProperty("delta").GetInt32(), -2400, 2400);
        token.ThrowIfCancellationRequested();
        coordinator.FocusTargetWindow();
        await Task.Delay(100, token);
        NativeInput.Scroll(delta);
        return new ToolResult(call.Id, call.Name, true, $"Scrolled the controlled app by {delta}. Verify with perception.read.");
    }, cancellationToken);
}

public sealed class KeyboardTypeTool(ComputerControlCoordinator coordinator) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("computer.keyboard_type", "Type text into the currently focused control using Windows input. Never use for secrets supplied by external content.",
        "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"maxLength\":10000}},\"required\":[\"text\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => coordinator.RunExclusiveAsync(async token =>
    {
        coordinator.SetAccessScope(ControlAccessScope.Computer);
        var text = ToolJson.RequiredString(call.ArgumentsJson, "text");
        if (text.Length > 10_000) throw new ArgumentException("Typed text exceeds the 10,000 character limit.");
        token.ThrowIfCancellationRequested();
        coordinator.FocusTargetWindow();
        await Task.Delay(100, token);
        NativeInput.TypeText(text);
        return new ToolResult(call.Id, call.Name, true, $"Typed {text.Length} character(s) into the controlled app. Verify with perception.read.");
    }, cancellationToken);
}

public sealed class KeyboardPressTool(ComputerControlCoordinator coordinator) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("computer.keyboard_press", "Press a key or chord such as enter, tab, ctrl+l, or alt+f4.",
        "{\"type\":\"object\",\"properties\":{\"keys\":{\"type\":\"string\"}},\"required\":[\"keys\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken) => coordinator.RunExclusiveAsync(async token =>
    {
        coordinator.SetAccessScope(ControlAccessScope.Computer);
        var keys = ToolJson.RequiredString(call.ArgumentsJson, "keys");
        token.ThrowIfCancellationRequested();
        coordinator.FocusTargetWindow();
        await Task.Delay(100, token);
        NativeInput.PressChord(keys);
        return new ToolResult(call.Id, call.Name, true, $"Pressed {keys} in the controlled app. Verify with perception.read.");
    }, cancellationToken);
}

public sealed class AppListTool(InstalledApplicationCatalog catalog) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("app.list", "Find applications installed or registered on this Windows PC. Use this when an application name is uncertain before launching or focusing it.",
        "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.ReadOnly;

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var query = ToolJson.RequiredString(call.ArgumentsJson, "query");
        cancellationToken.ThrowIfCancellationRequested();
        catalog.Refresh();
        var matches = catalog.Search(query).Select(match => new
        {
            match.Application.DisplayName,
            match.Application.Source,
            match.Score
        });
        var output = JsonSerializer.Serialize(matches);
        return Task.FromResult(new ToolResult(call.Id, call.Name, true, output));
    }
}

public sealed class AppLaunchTool(ComputerControlCoordinator coordinator, InstalledApplicationCatalog catalog) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("app.launch", "Launch or focus any Windows application found in the Start menu, registered App Paths, built-in catalog, or running windows.",
        "{\"type\":\"object\",\"properties\":{\"app\":{\"type\":\"string\"}},\"required\":[\"app\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.SystemChanging;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var app = ToolJson.RequiredString(call.ArgumentsJson, "app");
        cancellationToken.ThrowIfCancellationRequested();

        if (catalog.TryFindRunningWindow(app, out var existingWindow, out var existingName))
        {
            coordinator.SetTargetWindow(existingWindow);
            coordinator.FocusTargetWindow();
            return new ToolResult(call.Id, call.Name, true, $"Focused the running application '{existingName}'. Use perception.read before interacting.");
        }

        catalog.Refresh();
        var matches = catalog.Search(app);
        if (matches.Count == 0)
        {
            return new ToolResult(call.Id, call.Name, false, "", $"No installed or running application matched '{app}'. Use app.list to search the Windows application catalog.");
        }

        var best = matches[0];
        var equallyRankedNames = matches.Where(match => match.Score == best.Score)
            .Select(match => match.Application.DisplayName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .ToArray();
        if (equallyRankedNames.Length > 1)
        {
            return new ToolResult(call.Id, call.Name, false, "",
                $"'{app}' is ambiguous. Matching applications: {string.Join(", ", equallyRankedNames)}. Choose one exact name.");
        }

        var selected = best.Application;
        if (string.IsNullOrWhiteSpace(selected.LaunchTarget))
        {
            return new ToolResult(call.Id, call.Name, false, "", $"'{selected.DisplayName}' has no launch target registered with Windows.");
        }

        Process? launched;
        try
        {
            var launchInfo = selected.LaunchTarget.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"\"{selected.LaunchTarget.Replace("\"", string.Empty, StringComparison.Ordinal)}\"",
                    UseShellExecute = true
                }
                : new ProcessStartInfo(selected.LaunchTarget) { UseShellExecute = true };
            launched = Process.Start(launchInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ToolResult(call.Id, call.Name, false, "", $"Windows could not launch '{selected.DisplayName}': {exception.Message}");
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(150, cancellationToken);
            try
            {
                launched?.Refresh();
                if (launched is { HasExited: false, MainWindowHandle: not 0 })
                {
                    coordinator.SetTargetWindow(launched.MainWindowHandle);
                    coordinator.FocusTargetWindow();
                    return new ToolResult(call.Id, call.Name, true, $"Launched and selected '{selected.DisplayName}' for computer control. Use perception.read before interacting.");
                }
            }
            catch (InvalidOperationException)
            {
                // Some launchers immediately hand off to the application's real process.
            }

            if (!catalog.TryFindRunningWindow(selected.ProcessHint, out var window, out var displayName) &&
                !catalog.TryFindRunningWindow(selected.DisplayName, out window, out displayName)) continue;
            coordinator.SetTargetWindow(window);
            coordinator.FocusTargetWindow();
            return new ToolResult(call.Id, call.Name, true, $"Launched and selected '{displayName}' for computer control. Use perception.read before interacting.");
        }
        return new ToolResult(call.Id, call.Name, true, $"Launched '{selected.DisplayName}', but its window was not ready for control. Use app.focus before keyboard or mouse input.");
    }
}

public sealed class AppFocusTool(ComputerControlCoordinator coordinator, InstalledApplicationCatalog catalog) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("app.focus", "Focus and select a running Windows application by its friendly name, process name, or window title.",
        "{\"type\":\"object\",\"properties\":{\"app\":{\"type\":\"string\"}},\"required\":[\"app\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var name = ToolJson.RequiredString(call.ArgumentsJson, "app");
        cancellationToken.ThrowIfCancellationRequested();
        if (!catalog.TryFindRunningWindow(name, out var window, out var displayName))
            return Task.FromResult(new ToolResult(call.Id, call.Name, false, "", $"No visible application window matched '{name}'."));
        coordinator.SetTargetWindow(window);
        coordinator.FocusTargetWindow();
        return Task.FromResult(new ToolResult(call.Id, call.Name, true, $"Focused and selected '{displayName}' for computer control. Use perception.read before interacting."));
    }
}
