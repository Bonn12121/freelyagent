using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Freely.App;

/// <summary>A normal, freely resizable home for the built-in browser when it is popped out.</summary>
public sealed class BrowserWindow : Window
{
    private readonly Border _glow;
    private readonly TextBlock _subtitle;

    public BrowserWindow()
    {
        Title = "Freely Browser";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();

        var root = new Grid
        {
            RequestedTheme = ElementTheme.Dark
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Grid { Padding = new Thickness(14, 0, 142, 0), Background = new SolidColorBrush(ColorHelper.FromArgb(244, 24, 24, 26)) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = CreateMark();
        mark.VerticalAlignment = VerticalAlignment.Center;
        titleBar.Children.Add(mark);

        var labels = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        labels.Children.Add(new TextBlock { Text = "Freely Browser", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _subtitle = new TextBlock { Text = "Resizable window", FontSize = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 154, 154, 160)) };
        labels.Children.Add(_subtitle);
        Grid.SetColumn(labels, 1);
        titleBar.Children.Add(labels);

        var dockButton = new Button
        {
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            Content = new FontIcon { Glyph = "\uE8A7", FontSize = 13 },
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(dockButton, "Return to Freely");
        AutomationProperties.SetName(dockButton, "Return browser to Freely");
        dockButton.Click += (_, _) => DockRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(dockButton, 2);
        titleBar.Children.Add(dockButton);
        root.Children.Add(titleBar);
        SetTitleBar(titleBar);

        var browserHost = new Grid { Margin = new Thickness(11, 4, 11, 11) };
        _glow = new Border
        {
            Margin = new Thickness(-7),
            BorderThickness = new Thickness(7),
            CornerRadius = new CornerRadius(18),
            Opacity = 0,
            IsHitTestVisible = false,
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = ColorHelper.FromArgb(0, 10, 132, 255), Offset = 0 },
                    new GradientStop { Color = ColorHelper.FromArgb(220, 94, 92, 230), Offset = 0.23 },
                    new GradientStop { Color = ColorHelper.FromArgb(255, 10, 132, 255), Offset = 0.52 },
                    new GradientStop { Color = ColorHelper.FromArgb(220, 100, 210, 255), Offset = 0.78 },
                    new GradientStop { Color = ColorHelper.FromArgb(0, 10, 132, 255), Offset = 1 }
                }
            }
        };
        browserHost.Children.Add(_glow);

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(150, 10, 132, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 20, 20, 22)),
            Child = Browser
        };
        browserHost.Children.Add(frame);
        Grid.SetRow(browserHost, 1);
        root.Children.Add(browserHost);

        Content = root;

        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1080, 760));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }
    }

    public WebView2 Browser { get; } = new();
    public nint Handle => WindowNative.GetWindowHandle(this);
    public event EventHandler? DockRequested;

    public void SetControlActive(bool active)
    {
        _glow.Opacity = active ? 0.9 : 0;
        _subtitle.Text = active ? "AI control active · hold Left Shift to stop" : "Resizable window";
    }

    private static Border CreateMark()
    {
        return new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 132, 255)),
            Child = new TextBlock
            {
                Text = "✦",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }
}
