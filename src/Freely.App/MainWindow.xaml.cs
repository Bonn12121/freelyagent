using Freely.App.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace Freely.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Freely";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        ProviderLabel.Text = App.Services.ProviderDisplayName;
        App.Services.ProviderChanged += OnProviderChanged;
        Closed += (_, _) => App.Services.ProviderChanged -= OnProviderChanged;

        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        AppWindow.GetFromWindowId(windowId).Resize(new Windows.Graphics.SizeInt32(1180, 760));

        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        Navigate("chat");
    }

    private void OnProviderChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => ProviderLabel.Text = App.Services.ProviderDisplayName);

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag) Navigate(tag);
    }

    private void Navigate(string tag)
    {
        var page = tag switch
        {
            "chat" => typeof(ChatPage),
            "history" => typeof(HistoryPage),
            "tasks" => typeof(TasksPage),
            "memory" => typeof(MemoryPage),
            "agents" => typeof(AgentsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ChatPage)
        };
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        }
        ProviderLabel.Text = App.Services.ProviderDisplayName;
    }
}
