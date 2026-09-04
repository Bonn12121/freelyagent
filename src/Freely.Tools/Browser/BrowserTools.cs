using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Tools.Browser;

public interface IBrowserSession
{
    bool IsReady { get; }
    event EventHandler<bool>? VisibilityRequested;
    Task<string> OpenAsync(Uri uri, CancellationToken cancellationToken);
    Task<string> SnapshotAsync(CancellationToken cancellationToken);
    Task<string> ClickAsync(string elementId, CancellationToken cancellationToken);
    Task<string> TypeAsync(string elementId, string text, bool submit, CancellationToken cancellationToken);
    Task<string> ScrollAsync(int delta, CancellationToken cancellationToken);
    Task<string> PressAsync(string keys, CancellationToken cancellationToken);
    void RequestVisibility(bool visible);
}

public sealed class BrowserOpenTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.open", "Open a URL in the browser selected in Settings—Freely Browser or an installed browser—and return an accessibility/layout observation.",
        "{\"type\":\"object\",\"properties\":{\"url\":{\"type\":\"string\"}},\"required\":[\"url\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.SystemChanging;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var value = ToolJson.RequiredString(call.ArgumentsJson, "url");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return new(call.Id, call.Name, false, "", "Only absolute HTTP and HTTPS URLs are supported.");
            browser.RequestVisibility(true);
            return new(call.Id, call.Name, true, await browser.OpenAsync(uri, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or ArgumentException or JsonException)
        {
            return new(call.Id, call.Name, false, "", exception.Message);
        }
    }
}

public sealed class BrowserSnapshotTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.snapshot", "Read the selected browser's current page as visible text, controls, bounds, and short-lived semantic element IDs.",
        "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.ReadOnly;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try { return new(call.Id, call.Name, true, await browser.SnapshotAsync(cancellationToken).ConfigureAwait(false)); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException) { return new(call.Id, call.Name, false, "", exception.Message); }
    }
}

public sealed class BrowserClickTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.click", "Move the visible Windows pointer to a semantic element from browser.snapshot, physically click it, and return the changed page observation.",
        "{\"type\":\"object\",\"properties\":{\"elementId\":{\"type\":\"string\"}},\"required\":[\"elementId\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var id = ToolJson.RequiredString(call.ArgumentsJson, "elementId");
            browser.RequestVisibility(true);
            return new(call.Id, call.Name, true, await browser.ClickAsync(id, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException) { return new(call.Id, call.Name, false, "", exception.Message); }
    }
}

public sealed class BrowserTypeTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.type", "Physically click a semantic browser field, type through Windows keyboard input, optionally press Enter, and return the changed page observation.",
        "{\"type\":\"object\",\"properties\":{\"elementId\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"},\"submit\":{\"type\":\"boolean\"}},\"required\":[\"elementId\",\"text\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            using var json = JsonDocument.Parse(call.ArgumentsJson);
            var root = json.RootElement;
            var id = root.GetProperty("elementId").GetString() ?? throw new ArgumentException("elementId is required.");
            var text = root.GetProperty("text").GetString() ?? string.Empty;
            var submit = root.TryGetProperty("submit", out var submitValue) && submitValue.GetBoolean();
            browser.RequestVisibility(true);
            return new(call.Id, call.Name, true, await browser.TypeAsync(id, text, submit, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException) { return new(call.Id, call.Name, false, "", exception.Message); }
    }
}

public sealed class BrowserScrollTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.scroll", "Move the visible pointer over the page and physically turn the mouse wheel. Positive delta scrolls up; negative scrolls down.",
        "{\"type\":\"object\",\"properties\":{\"delta\":{\"type\":\"integer\",\"minimum\":-2400,\"maximum\":2400}},\"required\":[\"delta\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            using var json = JsonDocument.Parse(call.ArgumentsJson);
            var delta = Math.Clamp(json.RootElement.GetProperty("delta").GetInt32(), -2400, 2400);
            browser.RequestVisibility(true);
            return new(call.Id, call.Name, true, await browser.ScrollAsync(delta, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException)
        {
            return new(call.Id, call.Name, false, "", exception.Message);
        }
    }
}

public sealed class BrowserPressTool(IBrowserSession browser) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("browser.key_press", "Send a real Windows key or chord to the visible page, such as tab, enter, escape, ctrl+f, or pageDown.",
        "{\"type\":\"object\",\"properties\":{\"keys\":{\"type\":\"string\"}},\"required\":[\"keys\"],\"additionalProperties\":false}");
    public ToolRisk Risk => ToolRisk.Write;
    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var keys = ToolJson.RequiredString(call.ArgumentsJson, "keys");
            browser.RequestVisibility(true);
            return new(call.Id, call.Name, true, await browser.PressAsync(keys, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException)
        {
            return new(call.Id, call.Name, false, "", exception.Message);
        }
    }
}
