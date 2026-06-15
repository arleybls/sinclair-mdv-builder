using System.Windows;
using Wpf.Ui.Controls;

namespace MdvApp;

/// <summary>How to resolve a name clash when importing a file.</summary>
public enum ImportConflictResult
{
    Cancel,
    Overwrite,
    Rename,
}

/// <summary>Asks the user to overwrite, rename, or cancel when a file name already exists.</summary>
public partial class ConflictPromptWindow : FluentWindow
{
    public ImportConflictResult Result { get; private set; } = ImportConflictResult.Cancel;

    public ConflictPromptWindow(string fileName)
    {
        InitializeComponent();
        MessageText.Text = $"A file named \"{fileName}\" already exists in this cartridge. " +
                           "Overwrite it, import under a new name, or cancel?";
    }

    /// <summary>Show the prompt modally and return the chosen resolution.</summary>
    public static ImportConflictResult Ask(string fileName)
    {
        var window = new ConflictPromptWindow(fileName) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        return window.Result;
    }

    private void OnOverwrite(object sender, RoutedEventArgs e) => Close(ImportConflictResult.Overwrite);

    private void OnRename(object sender, RoutedEventArgs e) => Close(ImportConflictResult.Rename);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(ImportConflictResult.Cancel);

    private void Close(ImportConflictResult result)
    {
        Result = result;
        base.Close();
    }
}
