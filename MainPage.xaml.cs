using Microsoft.UI.Xaml.Controls;

namespace MediaForge;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        AppNavigation.SelectedItem = ConvertNavigationItem;
    }

    private async void AppNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        var targetPage = item.Tag?.ToString() == "history" ? typeof(HistoryPage) : typeof(ConverterPage);
        if (SectionFrame.Content?.GetType() != targetPage)
        {
            SectionFrame.Navigate(targetPage);
        }
        else if (SectionFrame.Content is HistoryPage historyPage)
        {
            await historyPage.RefreshAsync();
        }
    }
}
