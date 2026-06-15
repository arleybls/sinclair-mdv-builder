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
        // Intercept Enter before the DataGrid's own handling moves to the next row.
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ActivateSelectedFile();
            e.Handled = true;
        }
    }

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

    private void OnExtractFile(object sender, RoutedEventArgs e) =>
        AppActions.ExtractFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnDuplicateFile(object sender, RoutedEventArgs e) =>
        AppActions.DuplicateFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnRenameFile(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not MdvFileEntry)
            return;

        // Enable editing only for this explicit rename, then begin editing the Name cell.
        FilesGrid.IsReadOnly = false;
        FilesGrid.CurrentCell = new DataGridCellInfo(FilesGrid.SelectedItem, FilesGrid.Columns[1]);
        FilesGrid.BeginEdit();
    }

    private void OnPreparingNameEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is TextBox box)
            Dispatcher.BeginInvoke(
                new Action(() => { box.Focus(); box.SelectAll(); }),
                System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnNameEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        string? newName = e.EditAction == DataGridEditAction.Commit && e.EditingElement is TextBox box
            ? box.Text
            : null;
        var file = e.Row.Item as MdvFileEntry;

        // Defer until the grid finishes its edit transaction, then rebuild / restore read-only.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FilesGrid.IsReadOnly = true;
            if (newName != null && file != null)
                AppActions.RenameFileTo(file, newName);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnDeleteFile(object sender, RoutedEventArgs e) =>
        AppActions.DeleteFile(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnSetExecutable(object sender, RoutedEventArgs e) =>
        AppActions.ToggleExecutable(FilesGrid.SelectedItem as MdvFileEntry);

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnSave(object sender, RoutedEventArgs e) => AppActions.SaveCartridge();

    private void OnSaveAs(object sender, RoutedEventArgs e) => AppActions.SaveCartridgeAs();
}
