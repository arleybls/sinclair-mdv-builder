using System.Windows.Controls;
using MdvApp.Models;

namespace MdvApp.Pages;

public partial class MediaInfoPage : Page
{
    public MediaInfoPage()
    {
        InitializeComponent();
        InfoItems.ItemsSource = CartridgeSampleData.MediaInfo;
    }
}
