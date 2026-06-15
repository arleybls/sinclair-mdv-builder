using System.Windows;
using System.Windows.Controls;

namespace MdvApp.Pages;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnNewEmptyCartridge(object sender, RoutedEventArgs e) => AppActions.NewEmptyCartridge();

    private void OnNewFromFiles(object sender, RoutedEventArgs e) => AppActions.NewCartridgeFromFiles();

    private void OnOpenCartridge(object sender, RoutedEventArgs e) => AppActions.OpenCartridge();
}
