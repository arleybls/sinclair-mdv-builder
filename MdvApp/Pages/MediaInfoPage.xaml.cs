using System.Collections.Generic;
using System.Windows.Controls;
using MdvApp.Models;
using MdvCore.Mdv;

namespace MdvApp.Pages;

public partial class MediaInfoPage : Page
{
    public MediaInfoPage()
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
            InfoItems.ItemsSource = null;
            return;
        }

        InfoItems.ItemsSource = new List<InfoRow>
        {
            new("Format", "MDV (native QL microdrive)"),
            new("Medium name", cart.MediumName),
            new("Medium id", $"0x{cart.MediumId:X4}"),
            new("Image size", $"{MdvCartridge.ImageSize:N0} bytes"),
            new("Sector size", $"{MdvCartridge.SectorSize} bytes"),
            new("Sector count", $"{MdvCartridge.SectorCount} (0–254)"),
            new("Used sectors", cart.UsedSectorCount.ToString()),
            new("Free sectors", cart.FreeSectorCount.ToString()),
            new("Damaged sectors", cart.DamagedSectorCount.ToString()),
            new("Files", cart.Files.Count.ToString()),
        };
    }
}
