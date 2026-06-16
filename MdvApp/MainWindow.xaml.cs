using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MdvApp.Pages;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const string BaseTitle = "Sinclair MDV Builder";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RootNavigation.Navigate(typeof(HomePage));
            UpdateTitle();
        };
        AppState.Changed += UpdateTitle;
        Closed += (_, _) => AppState.Changed -= UpdateTitle;
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

    private void UpdateTitle()
    {
        var cart = AppState.Current;
        string title = cart == null
            ? BaseTitle
            : $"{cart.MediumName}{(AppState.IsDirty ? " •" : "")} — {BaseTitle}";
        Title = title;
        AppTitleBar.Title = title;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            AppActions.SaveCartridgeAs();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O: AppActions.OpenCartridge(); e.Handled = true; break;
                case Key.N: AppActions.NewEmptyCartridge(); e.Handled = true; break;
                case Key.S: AppActions.SaveCartridge(); e.Handled = true; break;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Dropping onto the shell (Home / nav) opens the first .MDV. Dropping onto the
    // Cartridge page is handled there (imports the files) before it reaches here.
    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        string? mdv = paths.FirstOrDefault(p => p.EndsWith(".mdv", StringComparison.OrdinalIgnoreCase));
        if (mdv != null)
            AppActions.OpenPath(mdv);
    }
}
