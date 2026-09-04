using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Freely.App.ViewModels;

public sealed class MessageItem : INotifyPropertyChanged
{
    private string _content;
    private bool _activityComplete;

    public MessageItem(string role, string content)
    {
        Role = role;
        _content = content;
        IsUser = role.Equals("user", StringComparison.OrdinalIgnoreCase);
        IsTool = role.Equals("tool", StringComparison.OrdinalIgnoreCase);
        IsActivity = role.Equals("activity", StringComparison.OrdinalIgnoreCase);
    }

    public string Role { get; }
    public bool IsUser { get; }
    public bool IsTool { get; }
    public bool IsActivity { get; }
    public HorizontalAlignment Alignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public Brush Background => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
        IsUser ? "FreelyUserMessageBrush" : "TransparentBrush"];
    public Brush Foreground => IsTool || (IsActivity && !_activityComplete)
        ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["FreelyWorkingTextBrush"]
        : new SolidColorBrush(Microsoft.UI.Colors.White);
    public double Opacity => IsTool || (IsActivity && !_activityComplete) ? 0.78 : 1;
    public Thickness Padding => IsUser ? new Thickness(13, 9, 13, 9) : new Thickness(2, IsTool || IsActivity ? 2 : 6, 2, 6);
    public CornerRadius CornerRadius => IsUser ? new CornerRadius(16) : new CornerRadius(0);
    public double MaxWidth => IsUser ? 560 : 760;
    public double FontSize => IsTool || IsActivity ? 12 : 14;

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }
    }

    public void SetActivity(string content, bool complete)
    {
        _activityComplete = complete;
        Content = content;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
