using System.Windows;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>Asks for an executable file's data-space size, with Apply / Cancel.</summary>
public partial class DataSpacePromptWindow : FluentWindow
{
    public uint Value => DataSpaceBox.Value is double v && v > 0 ? (uint)v : 0;

    public DataSpacePromptWindow(uint initial)
    {
        InitializeComponent();
        DataSpaceBox.Value = initial;
        Loaded += (_, _) => DataSpaceBox.Focus();
    }

    /// <summary>Show modally; returns the entered data space, or null if cancelled.</summary>
    public static uint? Ask(uint initial)
    {
        var window = new DataSpacePromptWindow(initial) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private void OnApply(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
