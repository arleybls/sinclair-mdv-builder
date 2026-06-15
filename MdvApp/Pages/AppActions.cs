using System;
using System.Windows;
using MdvCore.Mdv;
using Microsoft.Win32;

namespace MdvApp.Pages;

/// <summary>
/// Shared shell actions: cross-page navigation, a stub dialog for not-yet-built
/// operations, and the Open Cartridge flow.
/// </summary>
internal static class AppActions
{
    public static void Navigate(Type pageType) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(pageType);

    public static void NotImplemented() =>
        MessageBox.Show(
            "This operation isn't implemented yet.",
            "Coming soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    /// <summary>Prompt for an .MDV file, load it, and reveal the cartridge sections.</summary>
    public static void OpenCartridge()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open microdrive image",
            Filter = "Microdrive image (*.mdv)|*.mdv|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        MdvCartridge cartridge;
        try
        {
            cartridge = MdvCartridge.Load(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open this cartridge:\n\n{ex.Message}",
                "Open failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        AppState.SetCurrent(cartridge);

        if (Application.Current.MainWindow is MainWindow main)
        {
            main.SetCartridgeAvailable(true);
            main.NavigateTo(typeof(CartridgePage));
        }
    }
}
