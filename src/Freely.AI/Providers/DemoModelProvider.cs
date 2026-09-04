using System.Runtime.CompilerServices;
using System.Text.Json;
using Freely.Agent.Models;
using Freely.Agent.Runtime;

namespace Freely.AI.Providers;

public sealed class DemoModelProvider : IModelProvider
{
    public string Id => "demo";
    public string DisplayName => "Demo (no model configured)";

    public async IAsyncEnumerable<ModelStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var latestToolResult = request.Messages.LastOrDefault() is { Role: MessageRole.Tool } toolMessage
            ? toolMessage
            : null;
        if (latestToolResult is not null)
        {
            ToolResult? result = null;
            try { result = JsonSerializer.Deserialize<ToolResult>(latestToolResult.Content); }
            catch (JsonException) { }

            var summary = result?.Success == true
                ? $"Done. {result.Output}"
                : $"I couldn't complete that action. {result?.Error ?? "The tool returned an invalid result."}";
            foreach (var word in summary.Split(' '))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(18, cancellationToken).ConfigureAwait(false);
                yield return new ModelStreamChunk(word + " ");
            }

            yield return new ModelStreamChunk(IsComplete: true);
            yield break;
        }

        var goal = request.Goal.Trim();
        var command = ParseCommand(goal) ?? ParseNaturalLanguage(goal);
        if (command is not null)
        {
            yield return new ModelStreamChunk(ToolCall: command, IsComplete: true);
            yield break;
        }

        const string welcome = "Freely is running in safe demo mode. Ask naturally, for example: “list the files in my Downloads folder,” “read C:\\notes.txt,” “open example.com,” or “run Get-Date in PowerShell.” Configure a provider in Settings for broader AI reasoning.";
        foreach (var word in welcome.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(12, cancellationToken).ConfigureAwait(false);
            yield return new ModelStreamChunk(word + " ");
        }

        yield return new ModelStreamChunk(IsComplete: true);
    }

    private static ToolCall? ParseCommand(string input)
    {
        var split = input.IndexOf(' ');
        var name = split < 0 ? input : input[..split];
        var value = split < 0 ? string.Empty : input[(split + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;

        return name.ToLowerInvariant() switch
        {
            "/files" => Call("folder.list", new { path = value }),
            "/read" => Call("file.read", new { path = value }),
            "/open" => Call("browser.open", new { url = NormalizeWebAddress(value) }),
            "/powershell" => Call("shell.powershell", new { command = value }),
            _ => null
        };
    }

    private static ToolCall? ParseNaturalLanguage(string input)
    {
        var normalized = input.Trim().TrimEnd('.', '?', '!');
        var lower = normalized.ToLowerInvariant();

        var perceptionPhrases = new[]
        {
            "what is on my screen", "what's on my screen", "read my screen", "read the screen",
            "read the active window", "inspect the active window", "what is in this window", "what's in this window"
        };
        if (perceptionPhrases.Any(phrase => lower.Equals(phrase, StringComparison.Ordinal)))
        {
            return Call("perception.read", new { target = "active_window", detail = "normal" });
        }

        var inspectFilePrefixes = new[] { "inspect file ", "inspect the file ", "analyze file ", "analyze the file " };
        foreach (var prefix in inspectFilePrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Call("perception.read", new { target = "file", path = normalized[prefix.Length..].Trim(' ', '\'', '"'), detail = "normal" });
            }
        }

        var folderPrefixes = new[]
        {
            "list files in ", "list the files in ", "show files in ", "show me the files in ",
            "what files are in ", "list folder ", "show folder "
        };
        foreach (var prefix in folderPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = ExpandKnownFolder(normalized[prefix.Length..].Trim());
                return Call("folder.list", new { path = value });
            }
        }

        var readPrefixes = new[] { "read file ", "read the file ", "read ", "show the contents of " };
        foreach (var prefix in readPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Call("file.read", new { path = normalized[prefix.Length..].Trim(' ', '\'', '"') });
            }
        }

        var shellPrefixes = new[] { "run in powershell ", "run powershell ", "powershell ", "execute in powershell " };
        foreach (var prefix in shellPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Call("shell.powershell", new { command = normalized[prefix.Length..].Trim() });
            }
        }

        var appPrefixes = new[] { "launch ", "start ", "open ", "go to " };
        foreach (var prefix in appPrefixes)
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var app = FirstTaskTarget(normalized[prefix.Length..]);
            if (!string.IsNullOrWhiteSpace(app) && !LooksLikeWebTarget(app) &&
                !app.StartsWith("website ", StringComparison.OrdinalIgnoreCase) &&
                !app.StartsWith("site ", StringComparison.OrdinalIgnoreCase) &&
                !app.StartsWith("url ", StringComparison.OrdinalIgnoreCase))
            {
                return Call("app.launch", new { app });
            }
        }

        var typePrefixes = new[] { "type ", "write " };
        foreach (var prefix in typePrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Call("computer.keyboard_type", new { text = normalized[prefix.Length..].Trim(' ', '\'', '"') });
            }
        }

        var pressPrefixes = new[] { "press ", "hit " };
        if (lower is "scroll page down" or "scroll the page down" or "scroll browser down")
        {
            return Call("browser.scroll", new { delta = -600 });
        }
        if (lower is "scroll page up" or "scroll the page up" or "scroll browser up")
        {
            return Call("browser.scroll", new { delta = 600 });
        }
        const string browserKeySuffix = " on the page";
        if (lower.StartsWith("press ", StringComparison.Ordinal) && lower.EndsWith(browserKeySuffix, StringComparison.Ordinal))
        {
            return Call("browser.key_press", new { keys = normalized[6..^browserKeySuffix.Length].Trim() });
        }
        foreach (var prefix in pressPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Call("computer.keyboard_press", new { keys = normalized[prefix.Length..].Trim() });
            }
        }

        var searchPrefixes = new[] { "search the web for ", "search web for ", "browse for ", "google " };
        foreach (var prefix in searchPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                var query = normalized[prefix.Length..].Trim();
                return Call("browser.open", new { url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query) });
            }
        }

        const string runPrefix = "run ";
        const string powerShellSuffix = " in powershell";
        if (lower.StartsWith(runPrefix, StringComparison.Ordinal) && lower.EndsWith(powerShellSuffix, StringComparison.Ordinal))
        {
            var command = normalized[runPrefix.Length..^powerShellSuffix.Length].Trim();
            if (!string.IsNullOrWhiteSpace(command)) return Call("shell.powershell", new { command });
        }

        var openPrefixes = new[] { "open website ", "open site ", "open url ", "go to ", "browse to ", "open " };
        foreach (var prefix in openPrefixes)
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var value = normalized[prefix.Length..].Trim(' ', '\'', '"');
            if (!value.Contains(' ') && !value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return Call("browser.open", new { url = uri.AbsoluteUri });
            }
        }

        return null;
    }

    private static string ExpandKnownFolder(string value)
    {
        var cleaned = value.Trim(' ', '\'', '"');
        if (cleaned.Equals("downloads", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("my downloads", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("downloads folder", StringComparison.OrdinalIgnoreCase) ||
            cleaned.Equals("my downloads folder", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        if (cleaned.Equals("documents", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("my documents", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (cleaned.Equals("desktop", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("my desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return cleaned;
    }

    private static ToolCall Call(string name, object arguments) =>
        new(Guid.NewGuid().ToString("N"), name, JsonSerializer.Serialize(arguments));

    private static string FirstTaskTarget(string value)
    {
        var target = value.Trim(' ', '\'', '"');
        foreach (var separator in new[] { " and ", ", then ", " then " })
        {
            var index = target.IndexOf(separator, StringComparison.OrdinalIgnoreCase);
            if (index > 0) target = target[..index].Trim();
        }
        return target;
    }

    private static bool LooksLikeWebTarget(string value) =>
        value.Contains("://", StringComparison.Ordinal) ||
        (!value.Contains(' ') && value.Contains('.', StringComparison.Ordinal));

    private static string NormalizeWebAddress(string value)
    {
        var cleaned = value.Trim(' ', '\'', '"');
        return cleaned.Contains("://", StringComparison.Ordinal) ? cleaned : "https://" + cleaned;
    }
}
