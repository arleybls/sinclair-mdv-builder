using MdvApp.Models;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>A read-only viewer for a file's bytes, with Hex and Text tabs.</summary>
public partial class FileViewerWindow : FluentWindow
{
    public FileViewerWindow(string fileName, byte[] data, bool preferHex)
    {
        InitializeComponent();

        Title = fileName;
        ViewerTitleBar.Title = $"{fileName}  ·  {data.Length:N0} bytes";

        HexList.ItemsSource = HexDump.ToRows(data);
        TextView.Text = HexDump.ToText(data);

        ViewTabs.SelectedIndex = preferHex ? 0 : 1;
    }
}
