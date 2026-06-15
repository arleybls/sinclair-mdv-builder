using System.Windows.Controls;
using MdvApp.Models;

namespace MdvApp.Pages;

public partial class SectorMapPage : Page
{
    public SectorMapPage()
    {
        InitializeComponent();
        SectorItems.ItemsSource = CartridgeSampleData.Sectors;
    }
}
