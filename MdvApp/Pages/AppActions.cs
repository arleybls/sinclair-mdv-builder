using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ICSharpCode.SharpZipLib.Zip;
using MdvCore.Mdv;
using Microsoft.Win32;

namespace MdvApp.Pages;

/// <summary>
/// Shared shell actions: cross-page navigation, a stub dialog for not-yet-built
/// operations, and the Open Cartridge flow.
/// </summary>
internal static class AppActions
{
    // QDOS/SMS ZIP extra-field tag and signatures (qlzip convention) carrying the 64-byte QL header.
    private const ushort QdosExtraFieldId = 0xFB4A;
    private const int QlHeaderSize = 64;
    private const int QlExtraPayloadSize = 8 + QlHeaderSize; // LongID(4) + ExtraID(4) + qdirect(64)

    private readonly record struct ImportEntry(string Display, byte[] Content, byte TypeCode, uint DataSpace);

    public static void Navigate(Type pageType) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(pageType);

    /// <summary>
    /// Before replacing the current cartridge, offer to save unsaved changes.
    /// Returns true if the caller may proceed, false if the operation should be cancelled.
    /// </summary>
    private static bool ConfirmReplaceCartridge()
    {
        if (!AppState.IsDirty)
            return true;

        switch (UnsavedChangesPromptWindow.Ask())
        {
            case UnsavedChangesResult.Save:
                SaveCartridge();
                return !AppState.IsDirty; // proceed only if the save actually completed
            case UnsavedChangesResult.Discard:
                return true;
            default:
                return false;
        }
    }

    public static void NotImplemented() =>
        MessageBox.Show(
            "This operation isn't implemented yet.",
            "Coming soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    /// <summary>Create a blank cartridge (prompting for a medium name) and reveal the cartridge sections.</summary>
    public static void NewEmptyCartridge()
    {
        if (!ConfirmReplaceCartridge())
            return;

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

    /// <summary>Create a new cartridge (prompting for a medium name) and pack in chosen host files.</summary>
    public static void NewCartridgeFromFiles()
    {
        if (!ConfirmReplaceCartridge())
            return;

        string? name = TextPromptWindow.Ask("New cartridge from files", "Medium name (up to 10 characters):", "EMPTY");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select files to add",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        var items = new List<ImportEntry>();
        var omitted = new List<string>();
        foreach (string path in dialog.FileNames)
        {
            string display = Path.GetFileName(path);
            try { items.Add(new ImportEntry(display, File.ReadAllBytes(path), 0, 0)); }
            catch (Exception ex) { omitted.Add($"{display} — could not read ({ex.Message})"); }
        }

        CreateCartridgeFromItems(name.Trim(), items, omitted);
    }

    /// <summary>Create a new cartridge (prompting for a medium name) and pack in files from a ZIP.</summary>
    public static void NewCartridgeFromZip()
    {
        if (!ConfirmReplaceCartridge())
            return;

        string? name = TextPromptWindow.Ask("New cartridge from ZIP", "Medium name (up to 10 characters):", "EMPTY");
        if (string.IsNullOrWhiteSpace(name))
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Select ZIP archive",
            Filter = "ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        var omitted = new List<string>();
        var items = ReadZipEntries(dialog.FileName, omitted);
        if (items == null)
            return;

        CreateCartridgeFromItems(name.Trim(), items, omitted);
    }

    /// <summary>Create an empty cartridge, pack in as many of the given items as fit, then open it.</summary>
    private static void CreateCartridgeFromItems(string mediumName, List<ImportEntry> items, List<string> omitted)
    {
        MdvCartridge cartridge;
        try
        {
            cartridge = MdvCartridge.CreateEmpty(mediumName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create the cartridge:\n\n{ex.Message}", "New cartridge",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var item in items)
        {
            string fileName = UniqueImportName(cartridge, MdvCartridge.CleanFileName(item.Display));
            if (!cartridge.WouldFit(item.Content.Length, fileName, overwriteExisting: false, out _, out _))
            {
                omitted.Add($"{item.Display} — not enough free space");
                continue;
            }

            try { cartridge = cartridge.ImportFile(fileName, item.Content, item.TypeCode, item.DataSpace); }
            catch (Exception ex) { omitted.Add($"{item.Display} — {ex.Message}"); }
        }

        AppState.SetCurrent(cartridge, isDirty: true);
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.SetCartridgeAvailable(true);
            main.NavigateTo(typeof(CartridgePage));
        }

        if (omitted.Count > 0)
        {
            MessageBox.Show(
                "These files were not added:\n\n" + string.Join("\n", omitted.Select(o => "• " + o)),
                "Some files were not added",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string UniqueImportName(MdvCartridge cartridge, string baseName)
    {
        if (cartridge.FindFile(baseName) == null)
            return baseName;
        int n = 2;
        string candidate;
        do { candidate = MdvCartridge.CleanFileName($"{baseName}_{n++}"); }
        while (cartridge.FindFile(candidate) != null);
        return candidate;
    }

    /// <summary>Close the current cartridge (offering to save unsaved changes first).</summary>
    public static void EjectCartridge()
    {
        if (AppState.Current == null)
            return;
        if (!ConfirmReplaceCartridge())
            return;

        AppState.SetCurrent(null);
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.SetCartridgeAvailable(false);
            main.NavigateTo(typeof(HomePage));
        }
    }

    /// <summary>Prompt for an .MDV file, load it, and reveal the cartridge sections.</summary>
    public static void OpenCartridge()
    {
        if (!ConfirmReplaceCartridge())
            return;

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
        if (AppState.Current == null)
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

        var items = new List<ImportEntry>();
        var omitted = new List<string>();
        foreach (string path in dialog.FileNames)
        {
            string display = Path.GetFileName(path);
            try { items.Add(new ImportEntry(display, File.ReadAllBytes(path), 0, 0)); }
            catch (Exception ex) { omitted.Add($"{display} — could not read ({ex.Message})"); }
        }

        ImportItems(items, omitted);
    }

    /// <summary>Import every file entry from a chosen ZIP, restoring QL attributes when present.</summary>
    public static void ImportFromZip()
    {
        if (AppState.Current == null)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Import from ZIP",
            Filter = "ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        var omitted = new List<string>();
        var items = ReadZipEntries(dialog.FileName, omitted);
        if (items == null)
            return; // could not open the archive (error already shown)

        ImportItems(items, omitted);
    }

    /// <summary>
    /// Import a batch of items into the open cartridge: resolve name clashes, pack in as many as
    /// fit (preserving QL type/data-space), then report anything left out.
    /// </summary>
    private static void ImportItems(List<ImportEntry> items, List<string> omitted)
    {
        var current = AppState.Current;
        if (current == null)
            return;

        bool imported = false;
        foreach (var item in items)
        {
            string name = MdvCartridge.CleanFileName(item.Display);
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

            // Keep going past an item that doesn't fit — a later, smaller one may still fit.
            if (!current.WouldFit(item.Content.Length, name, overwrite, out _, out _))
            {
                omitted.Add($"{item.Display} — not enough free space");
                continue;
            }

            try
            {
                current = current.ImportFile(name, item.Content, item.TypeCode, item.DataSpace, overwrite);
                imported = true;
            }
            catch (Exception ex)
            {
                omitted.Add($"{item.Display} — {ex.Message}");
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

    /// <summary>Export every file in the open cartridge to a chosen .zip archive.</summary>
    public static void ExportToZip()
    {
        var cartridge = AppState.Current;
        if (cartridge == null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export to ZIP",
            Filter = "ZIP archive (*.zip)|*.zip|All files (*.*)|*.*",
            FileName = SuggestZipName(cartridge),
            DefaultExt = ".zip",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var stream = File.Create(dialog.FileName);
            using var zip = new ZipOutputStream(stream);
            zip.SetLevel(6);
            foreach (var file in cartridge.Files)
            {
                byte[] data = cartridge.ReadFileData(file);
                var entry = new ZipEntry(file.Name)
                {
                    DateTime = DateTime.Now,
                    Size = data.Length,
                    // QL attributes in the QDOS (0xFB4A) extra field, for QL-aware tools.
                    ExtraData = BuildQlExtraField(MdvCartridge.BuildQlFileHeader(file)),
                };
                zip.PutNextEntry(entry);
                zip.Write(data, 0, data.Length);
                zip.CloseEntry();
            }
            zip.Finish();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export to ZIP:\n\n{ex.Message}", "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SuggestZipName(MdvCartridge cartridge)
    {
        string baseName = !string.IsNullOrEmpty(cartridge.SourcePath)
            ? Path.GetFileNameWithoutExtension(cartridge.SourcePath)
            : (string.IsNullOrWhiteSpace(cartridge.MediumName) ? "cartridge" : cartridge.MediumName.Trim());
        string safe = new string(baseName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return (string.IsNullOrEmpty(safe) ? "cartridge" : safe) + ".zip";
    }

    /// <summary>Read a ZIP's file entries (with QL attributes when present). Returns null on open error.</summary>
    private static List<ImportEntry>? ReadZipEntries(string path, List<string> omitted)
    {
        var items = new List<ImportEntry>();
        try
        {
            using var zip = new ZipFile(path);
            foreach (ZipEntry entry in zip)
            {
                if (!entry.IsFile)
                    continue;
                try
                {
                    using var input = zip.GetInputStream(entry);
                    using var buffer = new MemoryStream();
                    input.CopyTo(buffer);

                    string display = Path.GetFileName(entry.Name);
                    (byte typeCode, uint dataSpace) = ReadQlExtraField(entry.ExtraData);
                    items.Add(new ImportEntry(display, buffer.ToArray(), typeCode, dataSpace));
                }
                catch (Exception ex)
                {
                    omitted.Add($"{entry.Name} — could not read ({ex.Message})");
                }
            }
            return items;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the ZIP:\n\n{ex.Message}", "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>Build the QDOS (0xFB4A) ZIP extra-field block wrapping a 64-byte QL header.</summary>
    private static byte[] BuildQlExtraField(byte[] qlHeader)
    {
        var field = new byte[4 + QlExtraPayloadSize];
        field[0] = (byte)(QdosExtraFieldId & 0xFF);   // tag (little-endian)
        field[1] = (byte)(QdosExtraFieldId >> 8);
        field[2] = (byte)(QlExtraPayloadSize & 0xFF); // data size (little-endian)
        field[3] = (byte)(QlExtraPayloadSize >> 8);
        System.Text.Encoding.ASCII.GetBytes("QZHD").CopyTo(field, 4);  // LongID
        System.Text.Encoding.ASCII.GetBytes("QDOS").CopyTo(field, 8);  // ExtraID
        Array.Copy(qlHeader, 0, field, 12, QlHeaderSize);
        return field;
    }

    /// <summary>Scan a ZIP extra-data block for the QDOS field and read the QL type / data-space.</summary>
    private static (byte TypeCode, uint DataSpace) ReadQlExtraField(byte[]? extra)
    {
        if (extra == null)
            return (0, 0);

        int i = 0;
        while (i + 4 <= extra.Length)
        {
            int id = extra[i] | (extra[i + 1] << 8);
            int size = extra[i + 2] | (extra[i + 3] << 8);
            int dataStart = i + 4;
            if (dataStart + size > extra.Length)
                break;

            // The QL header (qdirect) follows the 8-byte LongID/ExtraID prefix.
            if (id == QdosExtraFieldId && size >= QlExtraPayloadSize)
                return MdvCartridge.ReadQlFileHeader(extra.AsSpan(dataStart + 8, QlHeaderSize));

            i = dataStart + size;
        }
        return (0, 0);
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
