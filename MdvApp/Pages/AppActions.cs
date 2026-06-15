using System;
using System.IO;
using System.Linq;
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

    /// <summary>Save the open cartridge back to the file it was loaded from (Save As if unknown).</summary>
    public static void SaveCartridge()
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        if (string.IsNullOrEmpty(cartridge.SourcePath))
        {
            SaveCartridgeAs();
            return;
        }

        try
        {
            cartridge.Save(cartridge.SourcePath);
            AppState.MarkSaved();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save this cartridge:\n\n{ex.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>Write the open cartridge to a chosen .MDV path (byte-exact copy of the loaded image).</summary>
    public static void SaveCartridgeAs()
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save microdrive image as",
            Filter = "Microdrive image (*.mdv)|*.mdv|All files (*.*)|*.*",
            FileName = SuggestFileName(cartridge),
            DefaultExt = ".mdv",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            cartridge.Save(dialog.FileName);
            AppState.MarkSaved();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save this cartridge:\n\n{ex.Message}",
                "Save failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string SuggestFileName(MdvCartridge cartridge)
    {
        if (!string.IsNullOrEmpty(cartridge.SourcePath))
            return Path.GetFileName(cartridge.SourcePath);

        string name = string.IsNullOrWhiteSpace(cartridge.MediumName) ? "cartridge" : cartridge.MediumName.Trim();
        string safe = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return (string.IsNullOrEmpty(safe) ? "cartridge" : safe) + ".mdv";
    }
}
