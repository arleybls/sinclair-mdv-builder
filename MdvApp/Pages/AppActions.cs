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

    /// <summary>Pick a host file and import it into the open cartridge (handles name clashes and capacity).</summary>
    public static void ImportFile()
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import file",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        byte[] content;
        try
        {
            content = File.ReadAllBytes(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the file:\n\n{ex.Message}", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string name = MdvCartridge.CleanFileName(Path.GetFileName(dialog.FileName));
        bool overwrite = false;

        while (cartridge.FindFile(name) != null)
        {
            switch (ConflictPromptWindow.Ask(name))
            {
                case ImportConflictResult.Overwrite:
                    overwrite = true;
                    break;
                case ImportConflictResult.Rename:
                    string? renamed = TextPromptWindow.Ask("Rename file", "New file name:", name);
                    if (renamed == null)
                        return;
                    name = MdvCartridge.CleanFileName(renamed);
                    continue;
                default:
                    return;
            }
            break;
        }

        if (!cartridge.WouldFit(content.Length, name, overwrite, out int needed, out int available))
        {
            MessageBox.Show(
                $"There isn't enough space to import this file.\n\nIt needs {needed} sectors but only {available} are available.",
                "Not enough space",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            var updated = cartridge.ImportFile(name, content, overwrite: overwrite);
            AppState.SetCurrent(updated, isDirty: true);
            (Application.Current.MainWindow as MainWindow)?.SetCartridgeAvailable(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import the file:\n\n{ex.Message}", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Toggle the selected file between executable (type 1) and data (type 0).</summary>
    public static void ToggleExecutable(MdvFileEntry? file)
    {
        var cartridge = AppState.Current;
        if (cartridge == null || file == null)
            return;

        bool makeExecutable = !file.IsExecutable;

        uint dataSpace = 0;
        if (makeExecutable)
        {
            uint? entered = DataSpacePromptWindow.Ask(file.DataSpace);
            if (entered == null)
                return; // cancelled
            dataSpace = entered.Value;
        }

        byte typeCode = (byte)(makeExecutable ? 1 : 0);

        try
        {
            var updated = cartridge.SetFileType(file.Name, typeCode, dataSpace);
            AppState.SetCurrent(updated, isDirty: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not change the file type:\n\n{ex.Message}", "Set executable",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Copy the selected file into the cartridge under a new, unique name.</summary>
    public static void DuplicateFile(MdvFileEntry? file)
    {
        var cartridge = AppState.Current;
        if (cartridge == null || file == null)
            return;

        byte[] content;
        try
        {
            content = cartridge.ReadFileData(file);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the file:\n\n{ex.Message}", "Duplicate failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string name = UniqueCopyName(cartridge, file.Name);

        if (!cartridge.WouldFit(content.Length, name, overwriteExisting: false, out int needed, out int available))
        {
            MessageBox.Show(
                $"There isn't enough space to duplicate this file.\n\nIt needs {needed} sectors but only {available} are available.",
                "Not enough space",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            var updated = cartridge.ImportFile(name, content, file.TypeCode, file.DataSpace, overwrite: false);
            AppState.SetCurrent(updated, isDirty: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not duplicate the file:\n\n{ex.Message}", "Duplicate failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string UniqueCopyName(MdvCartridge cartridge, string baseName)
    {
        string root = MdvCartridge.CleanFileName(baseName);
        string candidate = MdvCartridge.CleanFileName(root + "_copy");
        int n = 2;
        while (cartridge.FindFile(candidate) != null)
            candidate = MdvCartridge.CleanFileName(root + "_copy" + n++);
        return candidate;
    }

    /// <summary>Confirm, then remove the selected file from the open cartridge.</summary>
    public static void DeleteFile(MdvFileEntry? file)
    {
        var cartridge = AppState.Current;
        if (cartridge == null || file == null)
            return;

        var confirm = MessageBox.Show(
            $"Delete \"{file.Name}\" from this cartridge?",
            "Delete file",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var updated = cartridge.DeleteFile(file.Name);
            AppState.SetCurrent(updated, isDirty: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete the file:\n\n{ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Write the selected file's content bytes to a chosen path on the host.</summary>
    public static void ExtractFile(MdvFileEntry? file)
    {
        var cartridge = AppState.Current;
        if (cartridge == null || file == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Extract file",
            FileName = file.Name,
            Filter = "All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, cartridge.ReadFileData(file));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not extract this file:\n\n{ex.Message}",
                "Extract failed",
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
