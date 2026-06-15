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

    /// <summary>Create a blank cartridge (prompting for a medium name) and reveal the cartridge sections.</summary>
    public static void NewEmptyCartridge()
    {
        string? name = TextPromptWindow.Ask("New empty cartridge", "Medium name (up to 10 characters):", "EMPTY");
        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            var cartridge = MdvCartridge.CreateEmpty(name.Trim());
            AppState.SetCurrent(cartridge, isDirty: true);

            if (Application.Current.MainWindow is MainWindow main)
            {
                main.SetCartridgeAvailable(true);
                main.NavigateTo(typeof(CartridgePage));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create the cartridge:\n\n{ex.Message}", "New cartridge",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    /// <summary>
    /// Pick one or more host files and import as many as fit. Name clashes prompt
    /// overwrite/rename/cancel; files that don't fit (or can't be read) are reported at the end.
    /// </summary>
    public static void ImportFile()
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import files",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        var current = cartridge;
        bool imported = false;
        var omitted = new List<string>();

        foreach (string path in dialog.FileNames)
        {
            string display = Path.GetFileName(path);

            byte[] content;
            try
            {
                content = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                omitted.Add($"{display} — could not read ({ex.Message})");
                continue;
            }

            string name = MdvCartridge.CleanFileName(display);
            bool overwrite = false;
            bool skip = false;

            while (current.FindFile(name) != null)
            {
                switch (ConflictPromptWindow.Ask(name))
                {
                    case ImportConflictResult.Overwrite:
                        overwrite = true;
                        break;
                    case ImportConflictResult.Rename:
                        string? renamed = TextPromptWindow.Ask("Rename file", "New file name:", name);
                        if (renamed == null) { skip = true; break; }
                        name = MdvCartridge.CleanFileName(renamed);
                        continue;
                    default:
                        skip = true;
                        break;
                }
                break;
            }

            if (skip)
                continue; // user chose not to import this one

            // Keep going past a file that doesn't fit — a later, smaller file may still fit.
            if (!current.WouldFit(content.Length, name, overwrite, out _, out _))
            {
                omitted.Add($"{display} — not enough free space");
                continue;
            }

            try
            {
                current = current.ImportFile(name, content, overwrite: overwrite);
                imported = true;
            }
            catch (Exception ex)
            {
                omitted.Add($"{display} — {ex.Message}");
            }
        }

        if (imported)
        {
            AppState.SetCurrent(current, isDirty: true);
            (Application.Current.MainWindow as MainWindow)?.SetCartridgeAvailable(true);
        }

        if (omitted.Count > 0)
        {
            MessageBox.Show(
                "These files were not imported:\n\n" + string.Join("\n", omitted.Select(o => "• " + o)),
                "Some files were not imported",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

    /// <summary>Rename the given file (used by inline editing in the file list).</summary>
    public static void RenameFileTo(MdvFileEntry file, string newName)
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        string clean = MdvCartridge.CleanFileName((newName ?? string.Empty).Trim());
        if (string.IsNullOrEmpty(clean) || string.Equals(clean, file.Name, StringComparison.Ordinal))
            return; // empty or unchanged

        var existing = cartridge.FindFile(clean);
        if (existing != null && !string.Equals(existing.Name, file.Name, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show($"A file named \"{clean}\" already exists.", "Rename",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var updated = cartridge.RenameFile(file.Name, clean);
            AppState.SetCurrent(updated, isDirty: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not rename the file:\n\n{ex.Message}", "Rename",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
