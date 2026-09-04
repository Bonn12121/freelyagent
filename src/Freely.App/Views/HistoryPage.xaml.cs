using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Freely.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        HistoryList.ItemsSource = await App.Services.Conversations.ListAsync();
    }
}

