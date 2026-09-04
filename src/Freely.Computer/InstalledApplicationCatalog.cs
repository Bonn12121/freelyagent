using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Freely.Computer;

public sealed record InstalledApplication(
    string Id,
    string DisplayName,
    string? LaunchTarget,
    string ProcessHint,
    string Source);

public sealed record ApplicationMatch(InstalledApplication Application, int Score);

public sealed class InstalledApplicationCatalog
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string AppsFolderShellId = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";
    private readonly object _sync = new();
    private IReadOnlyList<InstalledApplication> _applications = [];

    public InstalledApplicationCatalog() => Refresh();

    public IReadOnlyList<InstalledApplication> Applications
    {
        get { lock (_sync) return _applications; }
    }

    public void Refresh()
    {
        var applications = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        AddKnownApplications(applications);
        AddStartMenuApplications(applications);
        AddRegisteredAppPaths(applications);
        AddShellApplications(applications);
        lock (_sync)
        {
            _applications = applications.Values
                .OrderBy(application => application.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ApplicationMatch> Search(string query, int maximum = 8)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return [];
        return Applications
            .Select(application => new ApplicationMatch(application, Score(application, normalizedQuery)))
            .Where(match => match.Score >= 55)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Application.DisplayName.Length)
            .Take(Math.Clamp(maximum, 1, 25))
            .ToArray();
    }

    public bool TryFindRunningWindow(string query, out nint window, out string displayName)
    {
        var normalizedQuery = Normalize(query);
        var bestScore = 0;
        window = nint.Zero;
        displayName = query;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == nint.Zero) continue;
                var processName = process.ProcessName;
                var title = process.MainWindowTitle;
                var score = Math.Max(ScoreText(processName, normalizedQuery), ScoreText(title, normalizedQuery));
                if (score <= bestScore) continue;
                bestScore = score;
                window = process.MainWindowHandle;
                displayName = string.IsNullOrWhiteSpace(title) ? processName : title;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A process can exit or deny metadata access while the catalog is being read.
            }
        }
        return window != nint.Zero && bestScore >= 70;
    }

    private static void AddKnownApplications(IDictionary<string, InstalledApplication> applications)
    {
        Add(applications, "Notepad", "notepad.exe", "notepad", "windows");
        Add(applications, "Calculator", "calc.exe", "CalculatorApp", "windows");
        Add(applications, "Paint", "mspaint.exe", "mspaint", "windows");
        Add(applications, "File Explorer", "explorer.exe", "explorer", "windows");
        Add(applications, "Settings", "ms-settings:", "SystemSettings", "windows");
    }

    private static void AddStartMenuApplications(IDictionary<string, InstalledApplication> applications)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url" or ".appref-ms"))
                {
                    var displayName = Path.GetFileNameWithoutExtension(path);
                    if (IsMaintenanceShortcut(displayName)) continue;
                    Add(applications, displayName, path, displayName, "start_menu");
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                Debug.WriteLine($"Start Menu application discovery skipped a folder: {exception.Message}");
            }
        }
    }

    private static void AddRegisteredAppPaths(IDictionary<string, InstalledApplication> applications)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appPaths = baseKey.OpenSubKey(AppPathsKey);
                    if (appPaths is null) continue;
                    foreach (var keyName in appPaths.GetSubKeyNames())
                    {
                        using var appKey = appPaths.OpenSubKey(keyName);
                        var target = Environment.ExpandEnvironmentVariables(appKey?.GetValue(null)?.ToString() ?? string.Empty).Trim('"');
                        if (!File.Exists(target)) continue;
                        Add(applications, Path.GetFileNameWithoutExtension(keyName), target,
                            Path.GetFileNameWithoutExtension(target), "app_paths");
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    Debug.WriteLine($"App Paths discovery skipped a registry view: {exception.Message}");
                }
            }
        }
    }

    private static void AddShellApplications(IDictionary<string, InstalledApplication> applications)
    {
        object? shellObject = null;
        object? folderObject = null;
        object? itemsObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return;
            dynamic shell = shellObject;
            folderObject = shell.NameSpace(AppsFolderShellId);
            if (folderObject is null) return;
            dynamic folder = folderObject;
            itemsObject = folder.Items();
            dynamic items = itemsObject;
            var count = (int)items.Count;
            for (var index = 0; index < count; index++)
            {
                object? itemObject = null;
                try
                {
                    itemObject = items.Item(index);
                    dynamic item = itemObject;
                    var displayName = ((string?)item.Name)?.Trim() ?? string.Empty;
                    var appUserModelId = ((string?)item.Path)?.Trim() ?? string.Empty;
                    if (displayName.Length == 0 || appUserModelId.Length == 0 || IsMaintenanceShortcut(displayName)) continue;
                    Add(applications, displayName, $"shell:AppsFolder\\{appUserModelId}", displayName, "apps_folder");
                }
                finally
                {
                    ReleaseComObject(itemObject);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Windows AppsFolder discovery was unavailable: {exception.Message}");
        }
        finally
        {
            ReleaseComObject(itemsObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static void Add(IDictionary<string, InstalledApplication> applications, string displayName, string launchTarget, string processHint, string source)
    {
        var id = $"{source}|{launchTarget}";
        applications.TryAdd(id, new InstalledApplication(id, displayName.Trim(), launchTarget, processHint.Trim(), source));
    }

    private static int Score(InstalledApplication application, string normalizedQuery) => Math.Max(
        ScoreText(application.DisplayName, normalizedQuery),
        ScoreText(application.ProcessHint, normalizedQuery));

    private static int ScoreText(string value, string normalizedQuery)
    {
        var normalizedValue = Normalize(value);
        if (normalizedValue == normalizedQuery) return 100;
        if (normalizedValue.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 88;
        if (normalizedValue.Contains(normalizedQuery, StringComparison.Ordinal)) return 78;
        if (normalizedQuery.Contains(normalizedValue, StringComparison.Ordinal) && normalizedValue.Length >= 4) return 68;
        var queryWords = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return queryWords.Length > 0 && queryWords.All(word => normalizedValue.Contains(word, StringComparison.Ordinal)) ? 65 : 0;
    }

    private static string Normalize(string value)
    {
        var characters = value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsMaintenanceShortcut(string displayName)
    {
        var lower = displayName.ToLowerInvariant();
        return lower.Contains("uninstall", StringComparison.Ordinal) || lower.Contains("update", StringComparison.Ordinal) ||
            lower.Contains("repair", StringComparison.Ordinal) || lower.Contains("help", StringComparison.Ordinal) ||
            lower.Contains("readme", StringComparison.Ordinal) || lower.Contains("license", StringComparison.Ordinal);
    }
}
