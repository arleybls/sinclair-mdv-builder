using System;
using System.IO;
using System.Linq;
using System.Windows;
using MdvApp.Pages;

namespace MdvApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();

        // Open an .mdv passed on the command line (e.g. double-click / "Open with").
        string? path = e.Args.FirstOrDefault(
            a => a.EndsWith(".mdv", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (path != null)
            window.Loaded += (_, _) => AppActions.OpenPath(path);

        window.Show();
    }
}
