using System.Diagnostics;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.Tools.Shell;

public sealed class PowerShellTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new(
        "shell.powershell",
        "Run one PowerShell command after explicit confirmation and return its output.",
        "{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\"}},\"required\":[\"command\"],\"additionalProperties\":false}");

    public ToolRisk Risk => ToolRisk.Unknown;

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        try
        {
            var command = ToolJson.RequiredString(call.ArgumentsJson, "command");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell could not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            var error = await standardError.ConfigureAwait(false);
            return new(call.Id, call.Name, process.ExitCode == 0, output, string.IsNullOrWhiteSpace(error) ? null : error);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or System.Text.Json.JsonException)
        {
            return new(call.Id, call.Name, false, "", exception.Message);
        }
    }
}
