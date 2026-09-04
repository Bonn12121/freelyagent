using System.Collections.ObjectModel;
using Freely.Agent.Models;
using Freely.App.Services;
using Freely.App.ViewModels;
using Freely.Security.Permissions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Freely.App.Views;

public sealed partial class ChatPage : Page
{
    private CancellationTokenSource? _taskCancellation;
    private CancellationTokenSource? _dictationCancellation;
    private string? _conversationId;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _activityTimer;
    private DateTimeOffset _taskStarted;
    private MessageItem? _activityMessage;
    private string _activityPhase = "Reasoning";
    private string? _activityFailure;
    private BrowserWindow? _browserWindow;
    private bool _isUnloading;

    public ChatPage()
    {
        InitializeComponent();
        Messages.Add(new MessageItem("assistant", "Hello, I’m Freely. I can help with tasks on this computer, and I’ll ask before sensitive actions."));
        _activityTimer = DispatcherQueue.CreateTimer();
        _activityTimer.Interval = TimeSpan.FromSeconds(1);
        _activityTimer.Tick += (_, _) => UpdateActivityTime(false);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ObservableCollection<MessageItem> Messages { get; } = [];

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = false;
        App.Services.Browser.Attach(AgentBrowser, App.WindowHandle);
        App.Services.Browser.VisibilityRequested += OnBrowserVisibilityRequested;
        App.Services.ComputerControl.EmergencyStopped += OnEmergencyStopped;
        App.Services.PermissionGate.PermissionRequested += OnPermissionRequested;
        App.Services.Runtime.ProgressChanged += OnProgressChanged;
        if (!App.Services.EmergencyStopAvailable)
        {
            ControlNotice.Title = App.Services.AppControlAlwaysAllowEnabled ? "App control is ready" : "Computer control is available";
            ControlNotice.Message = App.Services.AppControlAlwaysAllowEnabled
                ? "Freely can open and control Windows apps without approval pop-ups. The global Left Shift emergency stop could not be registered on this system; use Stop in Freely."
                : "App control actions require approval. The global Left Shift emergency stop could not be registered on this system; use Stop in Freely.";
        }
        else if (App.Services.AppControlAlwaysAllowEnabled && App.Services.BrowserAlwaysAllowEnabled)
        {
            ControlNotice.Title = "App and browser control are ready";
            ControlNotice.Message = "Freely can operate apps and its browser without step-by-step approval. The blue frame shows what it controls; hold Left Shift for one second to force stop.";
        }
        else if (App.Services.AppControlAlwaysAllowEnabled)
        {
            ControlNotice.Title = "App control is ready";
            ControlNotice.Message = "Freely can open, focus, click, type, and scroll in Windows apps without approval pop-ups. The blue frame shows what it controls; hold Left Shift for one second to force stop.";
        }
        else if (App.Services.BrowserAlwaysAllowEnabled)
        {
            ControlNotice.Title = "Browser control is always allowed";
            ControlNotice.Message = "Freely can navigate, click, type, scroll, and press keys in its browser without step-by-step approval. Hold Left Shift for one second to force stop.";
        }
        PromptBox.Focus(FocusState.Programmatic);
        RefreshModelButton();
        RefreshPermissionButton();
        UpdateSendButtonState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = true;
        _browserWindow?.Close();
        _browserWindow = null;
        App.Services.Browser.VisibilityRequested -= OnBrowserVisibilityRequested;
        App.Services.ComputerControl.EmergencyStopped -= OnEmergencyStopped;
        App.Services.Browser.Detach(AgentBrowser);
        App.Services.PermissionGate.PermissionRequested -= OnPermissionRequested;
        App.Services.Runtime.ProgressChanged -= OnProgressChanged;
        App.Services.Dictation.Stop();
    }

    private void OnBrowserVisibilityRequested(object? sender, bool visible)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_browserWindow is not null)
            {
                BrowserPanel.Visibility = Visibility.Collapsed;
                BrowserGlow.Visibility = Visibility.Collapsed;
                _browserWindow.SetControlActive(visible);
                if (visible) _browserWindow.Activate();
                return;
            }
            BrowserPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BrowserGlow.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BrowserControlTitle.Text = "Freely Browser";
            BrowserControlSubtitle.Text = visible
                ? "AI control active · hold Left Shift to stop"
                : "Built-in browser";
            var glow = (Storyboard)Resources["BrowserGlowPulse"];
            if (visible) glow.Begin();
            else glow.Stop();
        });
    }

    private void OnCloseBrowserClicked(object sender, RoutedEventArgs e)
    {
        App.Services.Browser.RequestVisibility(false);
        PromptBox.Focus(FocusState.Programmatic);
    }

    private void OnPopOutBrowserClicked(object sender, RoutedEventArgs e)
    {
        if (_browserWindow is not null)
        {
            _browserWindow.Activate();
            return;
        }

        var source = AgentBrowser.Source;
        var browserWindow = new BrowserWindow();
        _browserWindow = browserWindow;
        App.Services.Browser.Detach(AgentBrowser);
        App.Services.Browser.Attach(browserWindow.Browser, browserWindow.Handle);
        BrowserPanel.Visibility = Visibility.Collapsed;
        BrowserGlow.Visibility = Visibility.Collapsed;

        browserWindow.DockRequested += OnBrowserDockRequested;
        browserWindow.Closed += OnBrowserWindowClosed;
        browserWindow.Activate();
        if (source is not null) browserWindow.Browser.Source = source;
        browserWindow.SetControlActive(_taskCancellation is not null);
    }

    private void OnBrowserDockRequested(object? sender, EventArgs e) => _browserWindow?.Close();

    private void OnBrowserWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is not BrowserWindow browserWindow) return;
        var source = browserWindow.Browser.Source;
        browserWindow.DockRequested -= OnBrowserDockRequested;
        browserWindow.Closed -= OnBrowserWindowClosed;
        App.Services.Browser.Detach(browserWindow.Browser);
        _browserWindow = null;
        if (_isUnloading) return;

        App.Services.Browser.Attach(AgentBrowser, App.WindowHandle);
        if (source is not null) AgentBrowser.Source = source;
        App.Services.Browser.RequestVisibility(true);
    }

    private void OnEmergencyStopped(object? sender, EventArgs e)
    {
        _taskCancellation?.Cancel();
        App.Services.Voice.Stop();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
        {
            SetStatus("Force stopped — Left Shift", false);
            Messages.Add(new MessageItem("tool", "■ Computer and browser control force-stopped by Left Shift."));
        });
    }

    private void OnPromptTextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSendButtonState();

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_taskCancellation is not null)
        {
            _taskCancellation.Cancel();
            App.Services.Voice.Stop();
            return;
        }
        await SendAsync();
    }

    private void OnPermissionButtonClicked(object sender, RoutedEventArgs e)
    {
        RefreshPermissionButton();
        SetToolbarPillActive(PermissionButton, true);
        if (!PermissionFlyout.IsOpen)
        {
            PermissionFlyout.ShowAt(PermissionButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft
            });
        }
    }

    private void OnPermissionFlyoutClosed(object sender, object e) =>
        SetToolbarPillActive(PermissionButton, false);

    private async void OnAskApprovalClicked(object sender, RoutedEventArgs e) =>
        await SetPermissionModeAsync(ChatPermissionMode.AskForApproval);

    private async void OnApproveForMeClicked(object sender, RoutedEventArgs e) =>
        await SetPermissionModeAsync(ChatPermissionMode.ApproveForMe);

    private async void OnFullAccessClicked(object sender, RoutedEventArgs e) =>
        await SetPermissionModeAsync(ChatPermissionMode.FullAccess);

    private async Task SetPermissionModeAsync(ChatPermissionMode mode)
    {
        await App.Services.SavePermissionModeAsync(mode);
        RefreshPermissionButton();
        PermissionFlyout.Hide();
    }

    private void RefreshPermissionButton()
    {
        var mode = App.Services.PermissionMode;
        PermissionButtonText.Text = mode switch
        {
            ChatPermissionMode.ApproveForMe => "Approve for me",
            ChatPermissionMode.FullAccess => "Full access",
            _ => "Ask for approval"
        };
        AskApprovalCheck.Visibility = mode == ChatPermissionMode.AskForApproval ? Visibility.Visible : Visibility.Collapsed;
        ApproveForMeCheck.Visibility = mode == ChatPermissionMode.ApproveForMe ? Visibility.Visible : Visibility.Collapsed;
        FullAccessCheck.Visibility = mode == ChatPermissionMode.FullAccess ? Visibility.Visible : Visibility.Collapsed;
        PermissionButton.Foreground = mode == ChatPermissionMode.FullAccess
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 10, 132, 255))
            : new SolidColorBrush(Microsoft.UI.Colors.White);
    }

    private void OnModelButtonClicked(object sender, RoutedEventArgs e)
        => OpenModelFlyout();

    private void OpenModelFlyout()
    {
        if (ModelFlyout.IsOpen) return;

        ModelOptions.Children.Clear();
        if (App.Services.SavedModelProfiles.Count == 0)
        {
            ModelOptions.Children.Add(new TextBlock
            {
                Text = "No saved models yet",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 168, 168, 173)),
                FontSize = 12,
                Margin = new Thickness(8, 8, 8, 12)
            });
        }
        foreach (var profile in App.Services.SavedModelProfiles
                     .OrderBy(profile => ModelSortOrder(profile.ModelId))
                     .ThenBy(profile => FriendlyModelName(profile.ModelId), StringComparer.OrdinalIgnoreCase))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = FriendlyModelName(profile.ModelId),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 245)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var check = new Viewbox
            {
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = string.Equals(profile.Id, App.Services.SelectedModelProfileId, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed
            };
            var checkPath = new Polyline
            {
                Stroke = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 245, 245, 245)),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            checkPath.Points.Add(new Windows.Foundation.Point(2, 7));
            checkPath.Points.Add(new Windows.Foundation.Point(5.3, 10.2));
            checkPath.Points.Add(new Windows.Foundation.Point(12, 3));
            check.Child = checkPath;
            Grid.SetColumn(check, 1);
            row.Children.Add(check);

            var button = new Button
            {
                Style = (Style)Resources["ChatMenuRowButtonStyle"],
                Content = row,
                Height = 29,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            button.Click += async (_, _) =>
            {
                await App.Services.SelectModelAsync(profile.Id);
                RefreshModelButton();
                ModelFlyout.Hide();
            };
            ModelOptions.Children.Add(button);
        }

        SetModelPillActive(true);
        ModelFlyout.ShowAt(ModelButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight
        });
    }

    private void OnModelFlyoutClosed(object sender, object e) =>
        SetModelPillActive(false);

    private void OnManageModelsClicked(object sender, RoutedEventArgs e)
    {
        ModelFlyout.Hide();
        Frame.Navigate(typeof(SettingsPage));
    }

    private async void OnVoiceClicked(object sender, RoutedEventArgs e)
    {
        if (_dictationCancellation is not null)
        {
            _dictationCancellation.Cancel();
            return;
        }

        _dictationCancellation = new CancellationTokenSource();
        VoiceButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["FreelyAccentBrush"];
        VoiceButton.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        ToolTipService.SetToolTip(VoiceButton, "Listening… click to stop");
        AutomationProperties.SetName(VoiceButton, "Stop listening");
        SetStatus("Listening…", false);
        try
        {
            var text = await App.Services.Dictation.ListenOnceAsync(_dictationCancellation.Token);
            if (!string.IsNullOrWhiteSpace(text))
            {
                PromptBox.Text = string.IsNullOrWhiteSpace(PromptBox.Text) ? text : $"{PromptBox.Text.TrimEnd()} {text}";
                PromptBox.SelectionStart = PromptBox.Text.Length;
            }
            SetStatus("Ready", true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Ready", true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Microphone unavailable", false);
            Messages.Add(new MessageItem("assistant",
                $"Microphone unavailable. Allow microphone access in Windows Privacy settings, then try again. {exception.Message}"));
        }
        finally
        {
            _dictationCancellation.Dispose();
            _dictationCancellation = null;
            VoiceButton.ClearValue(Button.BackgroundProperty);
            VoiceButton.ClearValue(Button.ForegroundProperty);
            ToolTipService.SetToolTip(VoiceButton, "Speak to type");
            AutomationProperties.SetName(VoiceButton, "Dictate message");
            PromptBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnPromptPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var controlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shiftDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.S && controlDown && shiftDown)
        {
            e.Handled = true;
            await SendAsync();
            return;
        }

        if (e.Key != VirtualKey.Enter) return;

        e.Handled = true;
        if (controlDown || shiftDown)
        {
            InsertPromptLineBreak();
            return;
        }

        await SendAsync();
    }

    private void InsertPromptLineBreak()
    {
        var start = Math.Clamp(PromptBox.SelectionStart, 0, PromptBox.Text.Length);
        var length = Math.Clamp(PromptBox.SelectionLength, 0, PromptBox.Text.Length - start);
        PromptBox.Text = string.Concat(
            PromptBox.Text.AsSpan(0, start),
            Environment.NewLine,
            PromptBox.Text.AsSpan(start + length));
        PromptBox.SelectionStart = start + Environment.NewLine.Length;
        PromptBox.SelectionLength = 0;
    }

    private async Task SendAsync()
    {
        var prompt = PromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || _taskCancellation is not null) return;

        App.Services.Voice.Stop();

        PromptBox.Text = string.Empty;
        Messages.Add(new MessageItem("user", prompt));
        BeginActivity();
        var response = new MessageItem("assistant", string.Empty);
        Messages.Add(response);
        MessageList.ScrollIntoView(response);

        _conversationId ??= await App.Services.Conversations.CreateAsync(prompt);
        await App.Services.Conversations.AddMessageAsync(_conversationId, "user", prompt);

        _taskCancellation = new CancellationTokenSource();
        UpdateSendButtonState();
        ModelButton.IsEnabled = false;
        VoiceButton.IsEnabled = false;
        SendIcon.Visibility = Visibility.Collapsed;
        StopIcon.Visibility = Visibility.Visible;
        AutomationProperties.SetName(SendButton, "Stop generating");
        ToolTipService.SetToolTip(SendButton, "Stop");
        try
        {
            await foreach (var text in App.Services.Runtime.RunAsync(prompt, _taskCancellation.Token))
            {
                response.Content += text;
                MessageList.ScrollIntoView(response);
            }

            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                await App.Services.Conversations.AddMessageAsync(_conversationId, "assistant", response.Content.Trim());
                if (App.Services.VoiceResponsesEnabled)
                {
                    _ = SpeakResponseAsync(response.Content);
                }
            }
            EndActivity(true);
        }
        catch (OperationCanceledException)
        {
            response.Content += "\n\nStopped.";
            SetStatus("Stopped", false);
            EndActivity(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            response.Content = $"I couldn't contact the configured provider. {exception.Message}";
            SetStatus("Provider error", false);
            EndActivity(false);
        }
        finally
        {
            _taskCancellation.Dispose();
            _taskCancellation = null;
            ModelButton.IsEnabled = true;
            VoiceButton.IsEnabled = true;
            SendIcon.Visibility = Visibility.Visible;
            StopIcon.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(SendButton, "Send message");
            ToolTipService.SetToolTip(SendButton, "Send");
            UpdateSendButtonState();
            PromptBox.Focus(FocusState.Programmatic);
        }
    }

    private void UpdateSendButtonState()
    {
        var isActive = _taskCancellation is not null || !string.IsNullOrWhiteSpace(PromptBox.Text);
        SendButton.Background = isActive
            ? (Brush)Application.Current.Resources["FreelyAccentBrush"]
            : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 58, 58, 60));
        SendButton.Opacity = isActive ? 1 : 0.72;
    }

    private void OnProgressChanged(object? sender, AgentProgress progress)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var phase = ActivityLabel(progress);
            SetStatus(phase, progress.Status == AgentStatus.Completed);
            _activityPhase = phase;
            if (progress.ToolResult is { Success: false } failed)
            {
                _activityFailure = failed.Error ?? "The action could not be completed.";
            }
            else _activityFailure = null;
            UpdateActivityTime(false);
        });
    }

    private void OnPermissionRequested(object? sender, PermissionRequestEventArgs request)
    {
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Allow this action?",
                Content = new TextBlock
                {
                    Text = $"Tool: {request.Tool.Definition.Name}\nRisk: {request.Tool.Risk}\n\n{request.Call.ArgumentsJson}",
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    MaxWidth = 520
                },
                PrimaryButtonText = "Allow once",
                CloseButtonText = "Deny",
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            request.Resolve(result == ContentDialogResult.Primary);
        }))
        {
            request.Resolve(false);
        }
    }

    private void SetStatus(string text, bool success)
    {
        StatusText.Text = text;
        StatusDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            success ? "SystemFillColorSuccessBrush" : "SystemFillColorAttentionBrush"];
        var pulse = (Storyboard)Resources["WorkingPulse"];
        if (success || text is "Stopped" or "Provider error" or "Force stopped — Left Shift") pulse.Stop();
        else pulse.Begin();
    }

    private static async Task SpeakResponseAsync(string text)
    {
        try
        {
            await App.Services.Voice.SpeakAsync(text);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or FileNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"Voice response unavailable: {exception.Message}");
        }
    }

    private void BeginActivity()
    {
        _taskStarted = DateTimeOffset.UtcNow;
        _activityPhase = "Reasoning";
        _activityFailure = null;
        _activityMessage = new MessageItem("activity", string.Empty);
        Messages.Add(_activityMessage);
        UpdateActivityTime(false);
        MessageList.ScrollIntoView(_activityMessage);
        _activityTimer.Start();
    }

    private void EndActivity(bool completed)
    {
        _activityTimer.Stop();
        App.Services.ComputerControl.SetAccessScope(Freely.Computer.ControlAccessScope.None);
        ((Storyboard)Resources["BrowserGlowPulse"]).Stop();
        BrowserGlow.Visibility = Visibility.Collapsed;
        _browserWindow?.SetControlActive(false);
        BrowserControlTitle.Text = "Freely Browser";
        BrowserControlSubtitle.Text = "Built-in browser";
        _activityPhase = completed ? "Done" : "Stopped";
        _activityFailure = null;
        UpdateActivityTime(true);
    }

    private void UpdateActivityTime(bool finished)
    {
        var seconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - _taskStarted).TotalSeconds);
        var detail = string.IsNullOrWhiteSpace(_activityFailure) ? string.Empty : $"\n{_activityFailure}";
        _activityMessage?.SetActivity(
            $"{(finished ? "Worked" : "Working")} for {seconds}s\n{_activityPhase}{detail}",
            finished);
    }

    private void RefreshModelButton()
    {
        ModelButtonText.Text = string.IsNullOrWhiteSpace(App.Services.SelectedModel)
            ? "Default"
            : FriendlyModelName(App.Services.SelectedModel);
        SetModelPillActive(false);
    }

    private static void SetToolbarPillActive(Button button, bool active) =>
        button.Background = active
            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(54, 10, 132, 255))
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private void SetModelPillActive(bool active) =>
        ModelButton.Background = new SolidColorBrush(active
            ? Microsoft.UI.ColorHelper.FromArgb(54, 255, 255, 255)
            : Microsoft.UI.ColorHelper.FromArgb(32, 255, 255, 255));

    private static int ModelSortOrder(string modelId)
    {
        var name = FriendlyModelName(modelId);
        return name switch
        {
            "5.6 Sol" => 0,
            "5.6 Terra" => 1,
            "5.6 Luna" => 2,
            "5.5" => 3,
            "5.4" => 4,
            "5.4 Mini" => 5,
            _ => 100
        };
    }

    private static string FriendlyModelName(string modelId)
    {
        var normalized = modelId.Trim().ToLowerInvariant();
        if (normalized is "gpt-5.6-sol" or "5.6-sol" or "5.6 sol") return "5.6 Sol";
        if (normalized is "gpt-5.6-terra" or "5.6-terra" or "5.6 terra") return "5.6 Terra";
        if (normalized is "gpt-5.6-luna" or "5.6-luna" or "5.6 luna") return "5.6 Luna";
        if (normalized is "gpt-5.5" or "5.5") return "5.5";
        if (normalized is "gpt-5.4" or "5.4") return "5.4";
        if (normalized is "gpt-5.4-mini" or "5.4-mini" or "5.4 mini") return "5.4 Mini";
        return modelId;
    }

    private static string ActivityLabel(AgentProgress progress)
    {
        if (progress.Status == AgentStatus.Acting)
        {
            var toolName = progress.ToolResult?.ToolName ??
                (progress.Message.StartsWith("Using ", StringComparison.OrdinalIgnoreCase)
                    ? progress.Message[6..]
                    : progress.Message);
            if (toolName.StartsWith("perception.", StringComparison.OrdinalIgnoreCase) ||
                toolName is "file.read" or "folder.list") return "Analyzing";
            if (toolName.StartsWith("browser.", StringComparison.OrdinalIgnoreCase))
                return toolName is "browser.open" or "browser.snapshot" ? "Browsing" : "Executing";
            return "Executing";
        }

        return progress.Status switch
        {
            AgentStatus.Thinking or AgentStatus.Planning => "Reasoning",
            AgentStatus.Verifying => "Analyzing",
            AgentStatus.WaitingForPermission => "Waiting for approval",
            AgentStatus.Completed => "Done",
            AgentStatus.Failed or AgentStatus.Stopped => "Stopped",
            _ => "Working"
        };
    }
}
