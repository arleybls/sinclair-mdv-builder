using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MdvApp.Models;
using MdvCore.Mdv;

namespace MdvApp.Pages;

public partial class SectorMapPage : Page
{
    public SectorMapPage()
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
            SectorItems.ItemsSource = null;
            HighlightCaption.Visibility = Visibility.Collapsed;
            return;
        }

        var nameByFile = cart.Files.ToDictionary(f => f.FileNumber, f => f.Name);
        var failingVerify = cart.SectorsFailingVerify();

        byte? highlight = AppState.HighlightFileNumber;
        SectorItems.ItemsSource = cart.Sectors
            .Select(s => new SectorCellView(
                s,
                highlight is byte fn && s.FileNumber == fn,
                BuildToolTip(s, nameByFile, failingVerify.Contains(s.Index)),
                failingVerify.Contains(s.Index)))
            .ToList();

        if (highlight is byte file)
        {
            int count = cart.Sectors.Count(s => s.FileNumber == file);
            HighlightCaption.Text = $"Highlighting file #{file} ({count} sectors)";
            HighlightCaption.Visibility = Visibility.Visible;
            ClearHighlightButton.Visibility = Visibility.Visible;
        }
        else
        {
            HighlightCaption.Visibility = Visibility.Collapsed;
            ClearHighlightButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClearHighlight(object sender, RoutedEventArgs e) => AppState.SetHighlightFile(null);

    private static string BuildToolTip(MdvSectorInfo sector, IReadOnlyDictionary<byte, string> nameByFile,
        bool verifyFailed)
    {
        string relation = sector.State switch
        {
            MdvSectorState.Map => "Allocation map (sector 0)",
            MdvSectorState.Free => "Free",
            MdvSectorState.Damaged => "Damaged",
            _ when sector.FileNumber == 0 => $"Directory file · block {sector.FileBlock}",
            _ when nameByFile.TryGetValue(sector.FileNumber, out var name)
                => $"{name} (#{sector.FileNumber}) · block {sector.FileBlock}",
            _ => $"File #{sector.FileNumber} · block {sector.FileBlock}",
        };

        string verify = verifyFailed ? "\nVerify error: checksum mismatch (e.g. Minerva workaround)" : "";
        return $"Sector {sector.Index}\n{relation}{verify}";
    }
}
