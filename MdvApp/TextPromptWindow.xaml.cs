using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>A simple modal text-input dialog (used for renaming on import).</summary>
public partial class TextPromptWindow : FluentWindow
{
    public string Value => InputBox.Text;

    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptTitleBar.Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    /// <summary>Show modally; returns the entered text, or null if cancelled.</summary>
    public static string? Ask(string title, string prompt, string initialValue)
    {
        var window = new TextPromptWindow(title, prompt, initialValue) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
