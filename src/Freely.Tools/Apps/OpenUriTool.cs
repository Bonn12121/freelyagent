using System.Diagnostics;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Tools.Apps;

public sealed class OpenUriTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "app.open_uri",
        "Open an HTTP or HTTPS address in the user's default browser.",
        "{\"type\":\"object\",\"properties\":{\"uri\":{\"type\":\"string\"}},\"required\":[\"uri\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.SystemChanging;

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = ToolJson.RequiredString(call.ArgumentsJson, "uri");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Task.FromResult(new ToolResult(call.Id, call.Name, false, "", "Only absolute HTTP and HTTPS addresses are allowed."));
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(new ToolResult(call.Id, call.Name, true, $"Opened {uri.Host}."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.Text.Json.JsonException)
        {
            return Task.FromResult(new ToolResult(call.Id, call.Name, false, "", exception.Message));
        }
    }
}

