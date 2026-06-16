using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MdvCore.Mdv;
using Wpf.Ui.Appearance;

namespace MdvApp.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();

        ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        AllocationCombo.SelectedIndex = (int)MdvCartridge.AllocationStrategy;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0";
        AboutText.Text = $"Sinclair MDV Builder · v{version}";
    }

    private void OnAllocationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return; // ignore the initial selection set during construction
        MdvCartridge.AllocationStrategy = (MdvSectorStrategy)AllocationCombo.SelectedIndex;
    }

    private void OnThemeToggled(object sender, RoutedEventArgs e)
    {
        var theme = ThemeToggle.IsChecked == true ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme);
    }
}
