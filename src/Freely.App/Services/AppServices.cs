using Freely.AI.Providers;
using Freely.Agent.Runtime;
using Freely.App.Browser;
using Freely.Computer;
using Freely.Perception;
using Freely.Perception.Providers;
using Freely.Security.Permissions;
using Freely.Storage.Database;
using Freely.Storage.Repositories;
using Freely.Tools.Apps;
using Freely.Tools.Browser;
using Freely.Tools.Computer;
using Freely.Tools.Files;
using Freely.Tools.Perception;
using Freely.Tools.Shell;
using Freely.Voice;
using System.Text.Json;

namespace Freely.App.Services;

public sealed class AppServices
{
    private static readonly string[] BrowserToolNames =
    [
        "browser.open", "browser.snapshot", "browser.click", "browser.type", "browser.scroll", "browser.key_press"
    ];
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly List<IAgentTool> _tools;
    private readonly SwitchableModelProvider _modelProvider;
    private readonly ApiKeyVault _apiKeyVault = new();
    private string _providerId = "demo";
    private string _endpoint = string.Empty;
    private string? _sessionApiKey;

    public AppServices()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreelyAgent");
        Database = new FreelyDatabase(Path.Combine(appData, "freely.db"));
        Conversations = new ConversationRepository(Database);
        Settings = new SettingsRepository(Database);
        PermissionPolicy = new PermissionPolicy();
        PermissionGate = new PermissionGate(PermissionPolicy);
        Perception = new PerceptionManager([new UIAutomationPerceptionProvider(), new ScreenOcrPerceptionProvider(), new FilePerceptionProvider()]);
        ComputerControl = new ComputerControlCoordinator();
        ApplicationPerception = new ApplicationPerceptionSession(Perception, ComputerControl);
        InstalledApplications = new InstalledApplicationCatalog();
        EmergencyStop = new EmergencyStopMonitor(ComputerControl);
        Browser = new AppBrowserSession(ComputerControl, Perception);
        Voice = new WindowsVoiceService();
        Dictation = new WindowsDictationService();
        _tools =
        [
            new PerceptionReadTool(ApplicationPerception),
            new BrowserOpenTool(Browser), new BrowserSnapshotTool(Browser), new BrowserClickTool(Browser), new BrowserTypeTool(Browser),
            new BrowserScrollTool(Browser), new BrowserPressTool(Browser),
            new AppListTool(InstalledApplications), new AppLaunchTool(ComputerControl, InstalledApplications),
            new AppFocusTool(ComputerControl, InstalledApplications),
            new AppClickElementTool(ComputerControl, ApplicationPerception),
            new AppTypeElementTool(ComputerControl, ApplicationPerception),
            new MouseMoveTool(ComputerControl), new MouseClickTool(ComputerControl), new MouseScrollTool(ComputerControl),
            new KeyboardTypeTool(ComputerControl), new KeyboardPressTool(ComputerControl),
            new FileReadTool(), new FolderListTool(), new OpenUriTool(), new PowerShellTool()
        ];
        _modelProvider = new SwitchableModelProvider(new DemoModelProvider());
        Runtime = CreateRuntime(_modelProvider);
    }

    public FreelyDatabase Database { get; }
    public ConversationRepository Conversations { get; }
    public SettingsRepository Settings { get; }
    public PermissionPolicy PermissionPolicy { get; }
    public PermissionGate PermissionGate { get; }
    public PerceptionManager Perception { get; }
    public ApplicationPerceptionSession ApplicationPerception { get; }
    public ComputerControlCoordinator ComputerControl { get; }
    public InstalledApplicationCatalog InstalledApplications { get; }
    public EmergencyStopMonitor EmergencyStop { get; }
    public AppBrowserSession Browser { get; }
    public IVoiceService Voice { get; }
    public IDictationService Dictation { get; }
    public IAgentRuntime Runtime { get; }
    public string ProviderDisplayName { get; private set; } = "Demo";
    public IReadOnlyList<SavedModelProfile> SavedModelProfiles { get; private set; } = [];
    public IReadOnlyList<string> ConfiguredModels => SavedModelProfiles.Select(profile => profile.ModelId).ToArray();
    public string? SelectedModelProfileId { get; private set; }
    public string SelectedModel { get; private set; } = string.Empty;
    public bool VoiceResponsesEnabled { get; private set; } = true;
    public bool ProtocolModeEnabled { get; private set; }
    public bool EmergencyStopAvailable { get; private set; }
    public bool BrowserAlwaysAllowEnabled { get; private set; }
    public bool AppControlAlwaysAllowEnabled { get; private set; }
    public ChatPermissionMode PermissionMode { get; private set; } = ChatPermissionMode.AskForApproval;
    public event EventHandler? ProviderChanged;

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        try
        {
            EmergencyStop.Start();
            EmergencyStopAvailable = true;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Global emergency stop unavailable: {exception.Message}");
        }
        var provider = await Settings.GetAsync("provider") ?? "demo";
        var preset = ProviderCatalog.Get(provider);
        var endpoint = await Settings.GetAsync("endpoint") ?? preset.Endpoint;
        var model = await Settings.GetAsync("model") ?? preset.DefaultModel;
        var legacyModels = ReadModels(await Settings.GetAsync("models"), model);
        SavedModelProfiles = ReadProfiles(await Settings.GetAsync("modelProfiles"))
            .Select(profile => profile with { HasApiKey = !string.IsNullOrWhiteSpace(_apiKeyVault.Read(profile.Id)) })
            .ToArray();
        if (SavedModelProfiles.Count == 0 && !provider.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            SavedModelProfiles = legacyModels.Select(item => new SavedModelProfile(
                Guid.NewGuid().ToString("N"), provider, endpoint, item, false, false)).ToArray();
            await SaveProfilesAsync();
        }
        VoiceResponsesEnabled = !string.Equals(await Settings.GetAsync("voice.enabled"), "false", StringComparison.OrdinalIgnoreCase);
        Voice.SelectVoice(await Settings.GetAsync("voice.id"));
        ProtocolModeEnabled = string.Equals(await Settings.GetAsync("provider.protocolMode"), "true", StringComparison.OrdinalIgnoreCase);
        if (SavedModelProfiles.Count > 0 && SavedModelProfiles.All(profile => !profile.ProtocolMode) && ProtocolModeEnabled)
        {
            SavedModelProfiles = SavedModelProfiles.Select(profile => profile with { ProtocolMode = true }).ToArray();
            await SaveProfilesAsync();
        }
        var savedPermissionMode = await Settings.GetAsync("permissions.mode");
        if (!Enum.TryParse<ChatPermissionMode>(savedPermissionMode, true, out var permissionMode))
        {
            var browserAllowed = string.Equals(await Settings.GetAsync("permissions.browser.alwaysAllow"), "true", StringComparison.OrdinalIgnoreCase);
            var appAllowed = !string.Equals(await Settings.GetAsync("permissions.appControl.alwaysAllow"), "false", StringComparison.OrdinalIgnoreCase);
            permissionMode = browserAllowed && appAllowed ? ChatPermissionMode.ApproveForMe : ChatPermissionMode.AskForApproval;
        }
        ApplyPermissionMode(permissionMode);
        Browser.RefreshAvailableBrowsers();
        Browser.SetSelectedBrowser(await Settings.GetAsync("browser.selected"));
        var selectedProfileId = await Settings.GetAsync("selectedModelProfile");
        var selectedProfile = SavedModelProfiles.FirstOrDefault(profile => profile.Id == selectedProfileId)
            ?? SavedModelProfiles.FirstOrDefault(profile => string.Equals(profile.ModelId, model, StringComparison.OrdinalIgnoreCase))
            ?? SavedModelProfiles.FirstOrDefault();
        if (selectedProfile is null) ConfigureProvider("demo", string.Empty, string.Empty, null);
        else SelectModel(selectedProfile.Id);
    }

    public void ConfigureProvider(string provider, string endpoint, string model, string? apiKey, bool protocolMode = false)
        => ConfigureProvider(provider, endpoint, [model], model, apiKey, protocolMode);

    public void ConfigureProvider(
        string provider,
        string endpoint,
        IReadOnlyList<string> models,
        string selectedModel,
        string? apiKey,
        bool protocolMode = false)
    {
        var cleanedModels = models.Select(item => item.Trim()).Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!provider.Equals("demo", StringComparison.OrdinalIgnoreCase) && cleanedModels.Length == 0)
        {
            throw new ArgumentException("Add at least one model ID.", nameof(models));
        }
        var selected = cleanedModels.FirstOrDefault(item => string.Equals(item, selectedModel, StringComparison.OrdinalIgnoreCase))
            ?? cleanedModels.FirstOrDefault()
            ?? string.Empty;
        _providerId = provider;
        _endpoint = endpoint;
        _sessionApiKey = apiKey;
        SelectedModel = selected;
        ProtocolModeEnabled = protocolMode;
        SwitchProvider(selected);
    }

    public void SelectModel(string model)
    {
        var profile = SavedModelProfiles.FirstOrDefault(item => string.Equals(item.Id, model, StringComparison.OrdinalIgnoreCase))
            ?? SavedModelProfiles.FirstOrDefault(item => string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase));
        if (profile is null) throw new ArgumentException("Choose a saved model.", nameof(model));
        SelectedModelProfileId = profile.Id;
        _providerId = profile.ProviderId;
        _endpoint = profile.Endpoint;
        _sessionApiKey = _apiKeyVault.Read(profile.Id);
        SelectedModel = profile.ModelId;
        ProtocolModeEnabled = profile.ProtocolMode;
        SwitchProvider(profile.ModelId);
    }

    public async Task SelectModelAsync(string profileId)
    {
        SelectModel(profileId);
        await Settings.SetAsync("model", SelectedModel);
        await Settings.SetAsync("selectedModelProfile", SelectedModelProfileId ?? string.Empty);
        await Settings.SetAsync("provider", _providerId);
        await Settings.SetAsync("endpoint", _endpoint);
        await Settings.SetAsync("provider.protocolMode", ProtocolModeEnabled ? "true" : "false");
    }

    public async Task<SavedModelProfile> SaveModelProfileAsync(
        string? profileId,
        string provider,
        string endpoint,
        string modelId,
        string? apiKey,
        bool protocolMode,
        bool makeSelected = true)
    {
        provider = provider.Trim();
        endpoint = endpoint.Trim();
        modelId = modelId.Trim();
        if (provider.Equals("demo", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a provider before saving a model.");
        if (modelId.Length == 0) throw new ArgumentException("Enter a model ID.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)) throw new ArgumentException("Enter a valid absolute base URL.");

        var existing = SavedModelProfiles.FirstOrDefault(profile => profile.Id == profileId);
        var id = existing?.Id ?? Guid.NewGuid().ToString("N");
        var providerChanged = existing is not null &&
            !string.Equals(existing.ProviderId, provider, StringComparison.OrdinalIgnoreCase);
        var savedKey = providerChanged ? null : _apiKeyVault.Read(id);
        var replacementKey = string.IsNullOrWhiteSpace(apiKey) ? savedKey : apiKey.Trim();
        if (ProviderCatalog.Get(provider).RequiresApiKey && string.IsNullOrWhiteSpace(replacementKey))
            throw new ArgumentException("Enter an API key for this provider.");
        if (!string.IsNullOrWhiteSpace(apiKey)) _apiKeyVault.Write(id, apiKey.Trim());
        else if (providerChanged) _apiKeyVault.Delete(id);

        var profile = new SavedModelProfile(id, provider, endpoint, modelId, protocolMode, !string.IsNullOrWhiteSpace(replacementKey));
        var profiles = SavedModelProfiles.ToList();
        var index = profiles.FindIndex(item => item.Id == id);
        if (index >= 0) profiles[index] = profile;
        else profiles.Add(profile);
        SavedModelProfiles = profiles.ToArray();
        await SaveProfilesAsync();

        if (makeSelected) await SelectModelAsync(profile.Id);
        return profile;
    }

    public async Task DeleteModelProfileAsync(string profileId)
    {
        var profiles = SavedModelProfiles.Where(profile => profile.Id != profileId).ToArray();
        if (profiles.Length == SavedModelProfiles.Count) return;
        _apiKeyVault.Delete(profileId);
        SavedModelProfiles = profiles;
        await SaveProfilesAsync();

        if (SelectedModelProfileId == profileId)
        {
            if (profiles.Length > 0) await SelectModelAsync(profiles[0].Id);
            else await UseDemoAsync();
        }
    }

    public string? GetSavedApiKey(string? profileId) => string.IsNullOrWhiteSpace(profileId) ? null : _apiKeyVault.Read(profileId);

    public async Task UseDemoAsync()
    {
        SelectedModelProfileId = null;
        ConfigureProvider("demo", string.Empty, string.Empty, null);
        await Settings.SetAsync("selectedModelProfile", string.Empty);
        await Settings.SetAsync("model", string.Empty);
    }

    private void SwitchProvider(string model)
    {
        IModelProvider provider;
        if (_providerId.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            provider = new DemoModelProvider();
        }
        else
        {
            if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("The provider endpoint must be an absolute URL.", nameof(_endpoint));
            }

            if (!uri.AbsoluteUri.EndsWith('/')) uri = new Uri(uri.AbsoluteUri + "/");
            provider = new OpenAiCompatibleProvider(_httpClient,
                new OpenAiCompatibleOptions(_providerId, $"{ProviderCatalog.Get(_providerId).DisplayName} · {model}", uri,
                    model, _sessionApiKey, !ProtocolModeEnabled));
        }

        ProviderDisplayName = _providerId.Equals("demo", StringComparison.OrdinalIgnoreCase)
            ? "Demo"
            : $"{ProviderCatalog.Get(_providerId).DisplayName} · {model}";
        _modelProvider.SwitchTo(provider);
        ProviderChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveProviderAsync(string provider, string endpoint, string model, bool protocolMode)
        => await SaveProviderAsync(provider, endpoint, [model], model, protocolMode);

    public async Task SaveProviderAsync(
        string provider,
        string endpoint,
        IReadOnlyList<string> models,
        string selectedModel,
        bool protocolMode)
    {
        await Settings.SetAsync("provider", provider);
        await Settings.SetAsync("endpoint", endpoint);
        await Settings.SetAsync("model", selectedModel);
        await Settings.SetAsync("models", JsonSerializer.Serialize(models));
        await Settings.SetAsync("provider.protocolMode", protocolMode ? "true" : "false");
    }

    private async Task SaveProfilesAsync() =>
        await Settings.SetAsync("modelProfiles", JsonSerializer.Serialize(SavedModelProfiles));

    private static IReadOnlyList<SavedModelProfile> ReadProfiles(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<SavedModelProfile[]>(json)?
                    .Where(profile => !string.IsNullOrWhiteSpace(profile.Id) &&
                        !string.IsNullOrWhiteSpace(profile.ProviderId) &&
                        !string.IsNullOrWhiteSpace(profile.Endpoint) &&
                        !string.IsNullOrWhiteSpace(profile.ModelId))
                    .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadModels(string? json, string fallback)
    {
        try
        {
            var parsed = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<string[]>(json);
            var models = parsed?.Select(item => item.Trim()).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (models is { Length: > 0 }) return models;
        }
        catch (JsonException)
        {
            // Preserve compatibility with settings written before multi-model support.
        }
        return string.IsNullOrWhiteSpace(fallback) ? [] : [fallback];
    }

    public async Task SaveVoiceAsync(bool enabled, string? voiceId)
    {
        VoiceResponsesEnabled = enabled;
        Voice.SelectVoice(voiceId);
        if (!enabled) Voice.Stop();
        await Settings.SetAsync("voice.enabled", enabled ? "true" : "false");
        await Settings.SetAsync("voice.id", voiceId ?? string.Empty);
    }

    public async Task SaveBrowserAlwaysAllowAsync(bool enabled)
    {
        SetBrowserAlwaysAllow(enabled);
        await Settings.SetAsync("permissions.browser.alwaysAllow", enabled ? "true" : "false");
        PermissionMode = InferPermissionMode();
        await Settings.SetAsync("permissions.mode", PermissionMode.ToString());
    }

    public async Task SaveAppControlAlwaysAllowAsync(bool enabled)
    {
        SetAppControlAlwaysAllow(enabled);
        await Settings.SetAsync("permissions.appControl.alwaysAllow", enabled ? "true" : "false");
        PermissionMode = InferPermissionMode();
        await Settings.SetAsync("permissions.mode", PermissionMode.ToString());
    }

    public async Task SavePermissionModeAsync(ChatPermissionMode mode)
    {
        ApplyPermissionMode(mode);
        await Settings.SetAsync("permissions.mode", mode.ToString());
        await Settings.SetAsync("permissions.browser.alwaysAllow", BrowserAlwaysAllowEnabled ? "true" : "false");
        await Settings.SetAsync("permissions.appControl.alwaysAllow", AppControlAlwaysAllowEnabled ? "true" : "false");
    }

    private void ApplyPermissionMode(ChatPermissionMode mode)
    {
        PermissionMode = mode;
        PermissionPolicy.ReadOnly = PermissionLevel.Allow;
        PermissionPolicy.Write = mode == ChatPermissionMode.AskForApproval ? PermissionLevel.Ask : PermissionLevel.Allow;
        PermissionPolicy.SystemChanging = mode == ChatPermissionMode.AskForApproval ? PermissionLevel.Ask : PermissionLevel.Allow;
        PermissionPolicy.Unknown = mode == ChatPermissionMode.FullAccess ? PermissionLevel.Allow : PermissionLevel.Ask;
        PermissionPolicy.Administrator = mode == ChatPermissionMode.FullAccess ? PermissionLevel.Allow : PermissionLevel.Ask;
        PermissionPolicy.Destructive = mode == ChatPermissionMode.FullAccess ? PermissionLevel.Allow : PermissionLevel.Ask;
        SetBrowserAlwaysAllow(mode != ChatPermissionMode.AskForApproval);
        SetAppControlAlwaysAllow(mode != ChatPermissionMode.AskForApproval);
    }

    private ChatPermissionMode InferPermissionMode()
    {
        if (PermissionPolicy.ReadOnly == PermissionLevel.Allow && PermissionPolicy.Write == PermissionLevel.Allow &&
            PermissionPolicy.SystemChanging == PermissionLevel.Allow && PermissionPolicy.Unknown == PermissionLevel.Allow &&
            PermissionPolicy.Administrator == PermissionLevel.Allow && PermissionPolicy.Destructive == PermissionLevel.Allow &&
            BrowserAlwaysAllowEnabled && AppControlAlwaysAllowEnabled)
            return ChatPermissionMode.FullAccess;
        if (PermissionPolicy.ReadOnly == PermissionLevel.Allow && PermissionPolicy.Write == PermissionLevel.Allow &&
            PermissionPolicy.SystemChanging == PermissionLevel.Allow && BrowserAlwaysAllowEnabled && AppControlAlwaysAllowEnabled)
            return ChatPermissionMode.ApproveForMe;
        return ChatPermissionMode.AskForApproval;
    }

    public async Task SaveBrowserSelectionAsync(string browserId)
    {
        Browser.SetSelectedBrowser(browserId);
        await Settings.SetAsync("browser.selected", Browser.SelectedBrowserId);
    }

    private void SetBrowserAlwaysAllow(bool enabled)
    {
        BrowserAlwaysAllowEnabled = enabled;
        foreach (var toolName in BrowserToolNames)
        {
            if (enabled) PermissionPolicy.SetOverride(toolName, PermissionLevel.Allow);
            else PermissionPolicy.ClearOverride(toolName);
        }
    }

    private void SetAppControlAlwaysAllow(bool enabled)
    {
        AppControlAlwaysAllowEnabled = enabled;
        NativeAppControlPermissions.Apply(PermissionPolicy, enabled);
    }

    private AgentRuntime CreateRuntime(IModelProvider provider) => new(provider, PermissionGate, _tools);

    public void Shutdown()
    {
        Dictation.Dispose();
        Voice.Stop();
        EmergencyStop.Dispose();
        ComputerControl.Dispose();
        _httpClient.Dispose();
    }
}
