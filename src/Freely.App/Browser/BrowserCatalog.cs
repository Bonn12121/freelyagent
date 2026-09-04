using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Freely.App.Browser;

public sealed record BrowserChoice(string Id, string DisplayName, string? ExecutablePath, bool IsEmbedded);

public static partial class BrowserCatalog
{
    private const string BrowserClientsKey = @"SOFTWARE\Clients\StartMenuInternet";

    public static IReadOnlyList<BrowserChoice> Discover()
    {
        var browsers = new Dictionary<string, BrowserChoice>(StringComparer.OrdinalIgnoreCase)
        {
            ["freely"] = new BrowserChoice("freely", "Freely Browser", null, true)
        };
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var clients = baseKey.OpenSubKey(BrowserClientsKey);
                    if (clients is null) continue;
                    foreach (var subKeyName in clients.GetSubKeyNames())
                    {
                        using var browserKey = clients.OpenSubKey(subKeyName);
                        using var commandKey = browserKey?.OpenSubKey(@"shell\open\command");
                        var executable = ExtractExecutable(commandKey?.GetValue(null)?.ToString());
                        if (executable is null || !File.Exists(executable)) continue;
                        var displayName = browserKey?.GetValue(null)?.ToString();
                        if (string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith('@'))
                            displayName = Path.GetFileNameWithoutExtension(executable);
                        AddExternal(browsers, displayName, executable);
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    System.Diagnostics.Debug.WriteLine($"Browser registry discovery skipped: {exception.Message}");
                }
            }
        }

        foreach (var candidate in KnownInstallLocations())
        {
            if (File.Exists(candidate.Path)) AddExternal(browsers, candidate.Name, candidate.Path);
        }

        return browsers.Values
            .OrderByDescending(browser => browser.IsEmbedded)
            .ThenBy(browser => browser.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void AddExternal(IDictionary<string, BrowserChoice> browsers, string displayName, string executable)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executable));
        var id = "external|" + fullPath;
        browsers.TryAdd(id, new BrowserChoice(id, displayName.Trim(), fullPath, false));
    }

    private static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var match = ExecutableCommandPattern().Match(expanded);
        return match.Success ? match.Groups["path"].Value : null;
    }

    private static IEnumerable<(string Name, string Path)> KnownInstallLocations()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return ("Microsoft Edge", Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"));
        yield return ("Google Chrome", Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"));
        yield return ("Google Chrome", Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"));
        yield return ("Google Chrome", Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe"));
        yield return ("Mozilla Firefox", Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"));
        yield return ("Mozilla Firefox", Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe"));
        yield return ("Brave", Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        yield return ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"));
        yield return ("Vivaldi", Path.Combine(local, "Vivaldi", "Application", "vivaldi.exe"));
        yield return ("Opera", Path.Combine(local, "Programs", "Opera", "opera.exe"));
        yield return ("Opera GX", Path.Combine(local, "Programs", "Opera GX", "opera.exe"));
    }

    [GeneratedRegex("^(?:\\\"(?<path>[^\\\"]+\\.exe)\\\"|(?<path>.*?\\.exe))(?:\\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutableCommandPattern();
}
