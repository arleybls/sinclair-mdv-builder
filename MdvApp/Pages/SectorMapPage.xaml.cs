using System.Linq;
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
        SectorItems.ItemsSource = cart?.Sectors.Select(s => new SectorCellView(s)).ToList();
    }
}
