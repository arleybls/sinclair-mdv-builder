using System;
using System.Windows;
using System.Windows.Controls;
using MdvCore.Mdv;

namespace MdvApp.Pages;

public partial class CartridgePage : Page
{
    public CartridgePage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => AppState.Changed -= Refresh;
        AppState.Changed += Refresh;
    }

    private void Refresh()
    {
        var cart = AppState.Current;
        if (cart == null)
        {
            MediumNameText.Text = "No cartridge loaded.";
            FilesGrid.ItemsSource = null;
            SaveButton.IsEnabled = false;
            ShowDetail(null);
            return;
        }

        long freeBytes = (long)cart.FreeSectorCount * 512;
        MediumNameText.Text =
            $"Medium: {cart.MediumName}  ·  {cart.Files.Count} files  ·  " +
            $"Free: {cart.FreeSectorCount} sectors ({freeBytes:N0} bytes)";
        FilesGrid.ItemsSource = cart.Files;
        SaveButton.IsEnabled = AppState.IsDirty;
        ShowDetail(FilesGrid.SelectedItem as MdvFileEntry);
    }

    private void ShowDetail(MdvFileEntry? file)
    {
        DetailContent.DataContext = file;
        DetailContent.Visibility = file == null ? Visibility.Collapsed : Visibility.Visible;
        DetailPlaceholder.Visibility = file == null ? Visibility.Visible : Visibility.Collapsed;

        if (file == null || AppState.Current is null)
            return;

        SetExecButton.Content = file.IsExecutable ? "Set Data" : "Set Exec";

        try
        {
            byte[] data = AppState.Current.ReadFileData(file);
            ChecksumText.Text = $"CRC-32: {MdvApp.Models.Crc32.Compute(data):X8}";
        }
        catch
        {
            ChecksumText.Text = "CRC-32: unavailable";
        }

        TypeCodeText.Text = $"Type: {file.TypeLabel} (code {file.TypeCode})";

        HeaderInfoText.Text =
            $"Access: 0x{file.FileAccess:X2}\n" +
            $"Total length: {file.TotalLength:N0} bytes\n" +
            $"Data space: {file.DataSpace:N0} bytes\n" +
            $"Extra info: 0x{file.ExtraInfo:X8}\n" +
            $"Updated: {FormatQlDate(file.UpdateDate)}\n" +
            $"Referenced: {FormatQlDate(file.ReferenceDate)}\n" +
            $"Backed up: {FormatQlDate(file.BackupDate)}";
    }

    // QL timestamps are seconds since the QDOS epoch, 1961-01-01 00:00:00.
    private static string FormatQlDate(uint seconds)
    {
        if (seconds == 0)
            return "not set";
        var epoch = new DateTime(1961, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm");
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e) =>
        ShowDetail(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnFileActivated(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ActivateSelectedFile();

    private void OnFilesPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                ActivateSelectedFile();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Delete:
                AppActions.DeleteFile(FilesGrid.SelectedItem as MdvFileEntry);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.F2:
                RenameSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnFilesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            AppActions.ImportPaths(paths);
            e.Handled = true; // don't let the shell treat a dropped .mdv as "open"
        }
    }

    private void OnExtractAll(object sender, RoutedEventArgs e) => AppActions.ExtractAll();

    private void ActivateSelectedFile()
    {
        if (FilesGrid.SelectedItem is not MdvFileEntry file || AppState.Current is null)
            return;

        try
        {
            byte[] bytes = AppState.Current.ReadFileData(file);
            new FileViewerWindow(file.Name, bytes, preferHex: file.IsBinary)
            {
                Owner = Application.Current.MainWindow,
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not read this file:\n\n{ex.Message}",
                "Open file failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnShowOnSectorMap(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not MdvFileEntry file)
            return;

        AppState.SetHighlightFile(file.FileNumber);
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(SectorMapPage));
    }

    private void OnImportFile(object sender, RoutedEventArgs e) => AppActions.ImportFile();

    private void OnImportFromZip(object sender, RoutedEventArgs e) => AppActions.ImportFromZip();

    private void OnExtractFile(object sender, RoutedEventArgs e) =>
        AppActions.ExtractFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnDuplicateFile(object sender, RoutedEventArgs e) =>
        AppActions.DuplicateFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnInspectFile(object sender, RoutedEventArgs e) => ActivateSelectedFile();

    private void OnRenameFile(object sender, RoutedEventArgs e) => RenameSelected();

    private void RenameSelected()
    {
        if (FilesGrid.SelectedItem is not MdvFileEntry file)
            return;

        string? newName = TextPromptWindow.Ask("Rename file", "New file name:", file.Name);
        if (newName == null)
            return;

        AppActions.RenameFileTo(file, newName);
    }

    private void OnDeleteFile(object sender, RoutedEventArgs e) =>
        AppActions.DeleteFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnSetExecutable(object sender, RoutedEventArgs e) =>
        AppActions.ToggleExecutable(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnSave(object sender, RoutedEventArgs e) => AppActions.SaveCartridge();

    private void OnSaveAs(object sender, RoutedEventArgs e) => AppActions.SaveCartridgeAs();

    private void OnExportToZip(object sender, RoutedEventArgs e) => AppActions.ExportToZip();

    private void OnExportMinerva(object sender, RoutedEventArgs e) => AppActions.ExportMinervaCopy();
}
