using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;

namespace MdvApp.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();

        ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0";
        AboutText.Text = $"Sinclair MDV Builder · v{version} · UI shell (format engine pending)";
    }

    private void OnThemeToggled(object sender, RoutedEventArgs e)
    {
        var theme = ThemeToggle.IsChecked == true ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme);
    }
}
