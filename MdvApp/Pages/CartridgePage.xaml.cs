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

        try
        {
            byte[] data = AppState.Current.ReadFileData(file);
            ChecksumText.Text = $"CRC-32: {MdvApp.Models.Crc32.Compute(data):X8}";
        }
        catch
        {
            ChecksumText.Text = "CRC-32: unavailable";
        }
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
            new FileViewerWindow(file.Name, bytes, preferHex: file.IsExecutable)
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

    private void OnNotImplemented(object sender, RoutedEventArgs e) => AppActions.NotImplemented();

    private void OnSave(object sender, RoutedEventArgs e) => AppActions.SaveCartridge();

    private void OnSaveAs(object sender, RoutedEventArgs e) => AppActions.SaveCartridgeAs();
}
