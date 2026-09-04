using System.Collections.ObjectModel;
using Freely.AI.Providers;
using Freely.App.Services;
using Freely.Security.Permissions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Freely.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loading;
    private string? _editingProfileId;
    public ObservableCollection<SavedModelProfile> Models { get; } = [];

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loading = true;
        RefreshProfiles();
        var selected = Models.FirstOrDefault(profile => profile.Id == App.Services.SelectedModelProfileId)
            ?? Models.FirstOrDefault();
        ModelList.SelectedItem = selected;
        if (selected is not null) LoadProfile(selected);
        else LoadNewEditor();

        VoiceResponsesToggle.IsOn = App.Services.VoiceResponsesEnabled;
        BrowserAlwaysAllowToggle.IsOn = App.Services.BrowserAlwaysAllowEnabled;
        AppControlAlwaysAllowToggle.IsOn = App.Services.AppControlAlwaysAllowEnabled;
        ReadOnlyPermission.SelectedIndex = PermissionIndex(App.Services.PermissionPolicy.ReadOnly);
        WritePermission.SelectedIndex = PermissionIndex(App.Services.PermissionPolicy.Write);
        UnknownPermission.SelectedIndex = PermissionIndex(App.Services.PermissionPolicy.Unknown);
        PopulateBrowsers();
        PopulateVoices();
        _loading = false;
        VoiceBox.IsEnabled = VoiceResponsesToggle.IsOn;
    }

    private void RefreshProfiles(string? selectId = null)
    {
        Models.Clear();
        foreach (var profile in App.Services.SavedModelProfiles) Models.Add(profile);
        var id = selectId ?? App.Services.SelectedModelProfileId;
        if (!string.IsNullOrWhiteSpace(id)) ModelList.SelectedItem = Models.FirstOrDefault(profile => profile.Id == id);
    }

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelList.SelectedItem is not SavedModelProfile profile) return;
        LoadProfile(profile);
    }

    private void LoadProfile(SavedModelProfile profile)
    {
        _loading = true;
        _editingProfileId = profile.Id;
        SelectProvider(profile.ProviderId);
        EndpointBox.Text = profile.Endpoint;
        ModelBox.Text = profile.ModelId;
        ProtocolModeToggle.IsOn = profile.ProtocolMode;
        ApiKeyBox.Password = string.Empty;
        ApiKeySavedText.Text = profile.HasApiKey ? "API key saved securely" : "No API key saved";
        UpdateProviderControls(profile.ProviderId);
        _loading = false;
    }

    private void LoadNewEditor()
    {
        _loading = true;
        _editingProfileId = null;
        ModelList.SelectedItem = null;
        var provider = SelectedProvider();
        if (provider == "demo") provider = "ollama";
        SelectProvider(provider);
        var preset = ProviderCatalog.Get(provider);
        EndpointBox.Text = preset.Endpoint;
        ModelBox.Text = preset.DefaultModel;
        ApiKeyBox.Password = string.Empty;
        ApiKeySavedText.Text = "No API key saved";
        ProtocolModeToggle.IsOn = false;
        UpdateProviderControls(provider);
        _loading = false;
    }

    private void SelectProvider(string provider)
    {
        ProviderBox.SelectedItem = ProviderBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
            ?? ProviderBox.Items[0];
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = SelectedProvider();
        if (_loading) return;
        var preset = ProviderCatalog.Get(provider);
        EndpointBox.Text = preset.Endpoint;
        ApiKeyBox.Password = string.Empty;
        ApiKeySavedText.Text = _editingProfileId is not null &&
            App.Services.SavedModelProfiles.Any(profile => profile.Id == _editingProfileId && profile.HasApiKey)
                ? "API key saved securely; enter a new key only to replace it"
                : "No API key saved";
        UpdateProviderControls(provider);
    }

    private void OnLocalOnlyToggled(object sender, RoutedEventArgs e)
    {
        if (LocalOnlyToggle.IsOn && !ProviderCatalog.Get(SelectedProvider()).IsLocal)
            ProviderBox.SelectedIndex = 1;
    }

    private void UpdateProviderControls(string provider)
    {
        var preset = ProviderCatalog.Get(provider);
        var demo = provider == "demo";
        EndpointBox.IsEnabled = !demo;
        ModelBox.IsEnabled = !demo;
        ApiKeyBox.IsEnabled = !demo && preset.RequiresApiKey;
        ApiKeySavedText.Visibility = preset.RequiresApiKey ? Visibility.Visible : Visibility.Collapsed;
        ProtocolModeToggle.IsEnabled = !demo;
        LocalOnlyToggle.IsOn = preset.IsLocal;
    }

    private void OnNewModelClicked(object sender, RoutedEventArgs e) => LoadNewEditor();

    private async void OnSaveModelClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = await SaveCurrentModelAsync();
            ShowResult("Model saved", $"{profile.ModelId} and its provider settings are now available in the composer.", InfoBarSeverity.Success);
        }
        catch (ArgumentException exception)
        {
            ShowResult("Check the model settings", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async Task<SavedModelProfile> SaveCurrentModelAsync()
    {
        var profile = await App.Services.SaveModelProfileAsync(
            _editingProfileId,
            SelectedProvider(),
            EndpointBox.Text,
            ModelBox.Text,
            ApiKeyBox.Password,
            ProtocolModeToggle.IsOn);
        _editingProfileId = profile.Id;
        ApiKeyBox.Password = string.Empty;
        ApiKeySavedText.Text = profile.HasApiKey ? "API key saved securely" : "No API key saved";
        _loading = true;
        RefreshProfiles(profile.Id);
        _loading = false;
        return profile;
    }

    private async void OnRemoveModelClicked(object sender, RoutedEventArgs e)
    {
        if (ModelList.SelectedItem is not SavedModelProfile selected) return;
        await App.Services.DeleteModelProfileAsync(selected.Id);
        _loading = true;
        RefreshProfiles();
        var next = Models.FirstOrDefault(profile => profile.Id == App.Services.SelectedModelProfileId) ?? Models.FirstOrDefault();
        ModelList.SelectedItem = next;
        _loading = false;
        if (next is not null) LoadProfile(next);
        else LoadNewEditor();
        ShowResult("Model removed", $"{selected.ModelId} and its saved API key were removed.", InfoBarSeverity.Success);
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyPermissions();
            if (SelectedProvider() != "demo" && !string.IsNullOrWhiteSpace(ModelBox.Text))
                await SaveCurrentModelAsync();
            else if (Models.Count == 0)
                await App.Services.UseDemoAsync();

            await App.Services.SaveVoiceAsync(VoiceResponsesToggle.IsOn, SelectedVoiceId());
            await App.Services.SaveBrowserAlwaysAllowAsync(BrowserAlwaysAllowToggle.IsOn);
            await App.Services.SaveAppControlAlwaysAllowAsync(AppControlAlwaysAllowToggle.IsOn);
            await App.Services.SaveBrowserSelectionAsync(SelectedBrowserId());
            ShowResult("Settings saved", "Saved models and API keys will be restored the next time Freely starts.", InfoBarSeverity.Success);
        }
        catch (ArgumentException exception)
        {
            ShowResult("Check the provider settings", exception.Message, InfoBarSeverity.Error);
        }
    }

    private async void OnTestConnectionClicked(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider() == "demo")
        {
            ShowResult("Demo mode is ready", "No network connection is used.", InfoBarSeverity.Informational);
            return;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(EndpointBox.Text.Trim()), "models"));
            var apiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password)
                ? App.Services.GetSavedApiKey(_editingProfileId)
                : ApiKeyBox.Password;
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request);
            ShowResult(response.IsSuccessStatusCode ? "Connection succeeded" : "Provider responded with an error",
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                response.IsSuccessStatusCode ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            ShowResult("Connection failed", exception.Message, InfoBarSeverity.Error);
        }
    }

    private void ApplyPermissions()
    {
        var policy = App.Services.PermissionPolicy;
        policy.ReadOnly = SelectedPermission(ReadOnlyPermission);
        policy.Write = SelectedPermission(WritePermission);
        policy.SystemChanging = SelectedPermission(WritePermission);
        policy.Unknown = SelectedPermission(UnknownPermission);
        var fullAccess = App.Services.PermissionMode == ChatPermissionMode.FullAccess;
        policy.Administrator = fullAccess ? PermissionLevel.Allow : PermissionLevel.Ask;
        policy.Destructive = fullAccess ? PermissionLevel.Allow : PermissionLevel.Ask;
    }

    private static PermissionLevel SelectedPermission(ComboBox box) => box.SelectedIndex switch
    {
        0 => PermissionLevel.Allow,
        2 => PermissionLevel.Deny,
        _ => PermissionLevel.Ask
    };

    private static int PermissionIndex(PermissionLevel level) => level switch
    {
        PermissionLevel.Allow => 0,
        PermissionLevel.Deny => 2,
        _ => 1
    };

    private string SelectedProvider() => (ProviderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "demo";

    private void OnVoiceResponsesToggled(object sender, RoutedEventArgs e)
    {
        VoiceBox.IsEnabled = VoiceResponsesToggle.IsOn;
        if (!VoiceResponsesToggle.IsOn) App.Services.Voice.Stop();
    }

    private void PopulateVoices()
    {
        VoiceBox.Items.Clear();
        VoiceBox.Items.Add(new ComboBoxItem { Content = "Automatic — friendly male voice", Tag = string.Empty });
        foreach (var voice in App.Services.Voice.AvailableVoices)
        {
            VoiceBox.Items.Add(new ComboBoxItem
            {
                Content = voice.IsMale ? $"{voice.DisplayName} (male)" : voice.DisplayName,
                Tag = voice.Id
            });
        }

        var selectedId = App.Services.Voice.SelectedVoiceId;
        VoiceBox.SelectedItem = VoiceBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedId, StringComparison.Ordinal))
            ?? VoiceBox.Items[0];
    }

    private string? SelectedVoiceId()
    {
        var value = (VoiceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void PopulateBrowsers()
    {
        App.Services.Browser.RefreshAvailableBrowsers();
        BrowserChoiceBox.Items.Clear();
        foreach (var browser in App.Services.Browser.AvailableBrowsers)
        {
            BrowserChoiceBox.Items.Add(new ComboBoxItem
            {
                Content = browser.IsEmbedded ? browser.DisplayName : $"{browser.DisplayName} — installed",
                Tag = browser.Id
            });
        }
        BrowserChoiceBox.SelectedItem = BrowserChoiceBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), App.Services.Browser.SelectedBrowserId, StringComparison.OrdinalIgnoreCase))
            ?? BrowserChoiceBox.Items[0];
        BrowserDiscoveryText.Text = $"Found {Math.Max(0, App.Services.Browser.AvailableBrowsers.Count - 1)} installed browser(s). External browsers use Windows accessibility plus the visible mouse and keyboard.";
    }

    private string SelectedBrowserId() => (BrowserChoiceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "freely";

    private void ShowResult(string title, string message, InfoBarSeverity severity)
    {
        SaveResult.Title = title;
        SaveResult.Message = message;
        SaveResult.Severity = severity;
        SaveResult.IsOpen = true;
    }
}
