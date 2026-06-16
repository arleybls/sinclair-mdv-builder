using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace MdvApp.Pages;

public partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateHeroButton();
        Unloaded += (_, _) => AppState.Changed -= OnAppStateChanged;
        AppState.Changed += OnAppStateChanged;
    }

    private void OnAppStateChanged() => UpdateHeroButton();

    private void UpdateHeroButton()
    {
        bool loaded = AppState.Current != null;
        HeroButton.Content = loaded ? "Eject Cartridge" : "Open cartridge…";
        HeroButton.Icon = new SymbolIcon { Symbol = loaded ? SymbolRegular.Dismiss24 : SymbolRegular.FolderOpen24 };
        HeroButton.Appearance = loaded ? ControlAppearance.Danger : ControlAppearance.Primary;
        CartridgeNameText.Text = loaded ? AppState.Current!.MediumName : string.Empty;
    }

    private void OnHeroButton(object sender, RoutedEventArgs e)
    {
        if (AppState.Current != null)
            AppActions.EjectCartridge();
        else
            AppActions.OpenCartridge();
    }

    private void OnOpenCartridge(object sender, RoutedEventArgs e) => AppActions.OpenCartridge();

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnNewEmptyCartridge(object sender, RoutedEventArgs e) => AppActions.NewEmptyCartridge();

    private void OnNewFromFiles(object sender, RoutedEventArgs e) => AppActions.NewCartridgeFromFiles();

    private void OnNewFromZip(object sender, RoutedEventArgs e) => AppActions.NewCartridgeFromZip();
}
