using System.Windows;
using System.Windows.Controls;
using MdvApp.Models;

namespace MdvApp.Pages;

public partial class CartridgePage : Page
{
    public CartridgePage()
    {
        InitializeComponent();
        MediumNameText.Text = $"Medium: {CartridgeSampleData.MediumName}  ·  {CartridgeSampleData.Files.Count} files";
        FilesGrid.ItemsSource = CartridgeSampleData.Files;
    }

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();
}
