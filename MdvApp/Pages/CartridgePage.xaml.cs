using System.Windows;
using System.Windows.Controls;

namespace MdvApp.Pages;

public partial class CartridgePage : Page
{
    public CartridgePage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => AppState.Changed -= Refresh;
        AppState.Changed += Refresh;
    }

    private void Refresh()
    {
        var cart = AppState.Current;
        if (cart == null)
        {
            MediumNameText.Text = "No cartridge loaded.";
            FilesGrid.ItemsSource = null;
            return;
        }

        MediumNameText.Text = $"Medium: {cart.MediumName}  ·  {cart.Files.Count} files";
        FilesGrid.ItemsSource = cart.Files;
    }

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnOpenCartridge(object sender, RoutedEventArgs e) => AppActions.OpenCartridge();

    private void OnSaveAs(object sender, RoutedEventArgs e) => AppActions.SaveCartridgeAs();
}
