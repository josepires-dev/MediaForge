using System.Collections.ObjectModel;
using MediaForge.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MediaForge;

public sealed partial class HistoryPage : Page
{
    private readonly HistoryService _historyService = new();
    private readonly ObservableCollection<ActivityHistoryItem> _items = [];

    public HistoryPage()
    {
        InitializeComponent();
        ActivityList.ItemsSource = _items;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var history = await _historyService.ReadAsync();
            _items.Clear();
            foreach (var item in history)
            {
                _items.Add(item);
            }
            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            EmptyText.Text = $"Não foi possível carregar o histórico: {exception.Message}";
            EmptyText.Visibility = Visibility.Visible;
        }
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _historyService.ClearAsync();
            _items.Clear();
            EmptyText.Text = "Histórico limpo. Os ficheiros guardados não foram alterados.";
            EmptyText.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            EmptyText.Text = $"Não foi possível limpar o histórico: {exception.Message}";
            EmptyText.Visibility = Visibility.Visible;
        }
    }
}
