using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MdvApp.Models;

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

        byte? highlight = AppState.HighlightFileNumber;
        SectorItems.ItemsSource = cart.Sectors
            .Select(s => new SectorCellView(s, highlight is byte fn && s.FileNumber == fn))
            .ToList();

        if (highlight is byte file)
        {
            int count = cart.Sectors.Count(s => s.FileNumber == file);
            HighlightCaption.Text = $"Highlighting file #{file} ({count} sectors)";
            HighlightCaption.Visibility = Visibility.Visible;
        }
        else
        {
            HighlightCaption.Visibility = Visibility.Collapsed;
        }
    }
}
