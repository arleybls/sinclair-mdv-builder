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
}
