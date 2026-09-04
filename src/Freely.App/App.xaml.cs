using Freely.App.Services;
using Microsoft.UI.Xaml;

namespace Freely.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => System.Diagnostics.Debug.WriteLine(args.Exception);
    }

    public static AppServices Services { get; } = new();
    public static nint WindowHandle => Current is App { _window: not null } app
        ? WinRT.Interop.WindowNative.GetWindowHandle(app._window)
        : nint.Zero;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Services.InitializeAsync();
        _window = new MainWindow();
        _window.Closed += (_, _) => Services.Shutdown();
        _window.Activate();
    }
}
