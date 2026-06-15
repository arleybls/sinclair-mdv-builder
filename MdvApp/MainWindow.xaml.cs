using System;
using MdvApp.Pages;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RootNavigation.Navigate(typeof(HomePage));
    }

    /// <summary>Navigate the shell to a page type (used by in-page shortcut cards).</summary>
    public void NavigateTo(Type pageType) => RootNavigation.Navigate(pageType);

    /// <summary>
    /// Enables/disables the cartridge-dependent nav sections. They start disabled
    /// and should be turned on only once an image is created or loaded.
    /// </summary>
    public void SetCartridgeAvailable(bool available)
    {
        double opacity = available ? 1.0 : 0.36;
        foreach (var item in new[] { NavCartridge, NavSectorMap, NavMediaInfo })
        {
            item.IsEnabled = available;
            item.Opacity = opacity;
        }
    }
}
