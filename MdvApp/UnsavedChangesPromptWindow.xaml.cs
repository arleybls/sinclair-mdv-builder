using System.Windows;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>How to handle unsaved changes before an operation replaces the cartridge.</summary>
public enum UnsavedChangesResult
{
    Cancel,
    Save,
    Discard,
}

/// <summary>Prompts to save, discard, or cancel when replacing a cartridge with unsaved changes.</summary>
public partial class UnsavedChangesPromptWindow : FluentWindow
{
    public UnsavedChangesResult Result { get; private set; } = UnsavedChangesResult.Cancel;

    public UnsavedChangesPromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Show the prompt modally and return the chosen action.</summary>
    public static UnsavedChangesResult Ask()
    {
        var window = new UnsavedChangesPromptWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        return window.Result;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Close(UnsavedChangesResult.Save);

    private void OnDiscard(object sender, RoutedEventArgs e) => Close(UnsavedChangesResult.Discard);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(UnsavedChangesResult.Cancel);

    private void Close(UnsavedChangesResult result)
    {
        Result = result;
        base.Close();
    }
}
