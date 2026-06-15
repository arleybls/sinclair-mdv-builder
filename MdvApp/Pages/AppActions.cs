using System;
using System.Windows;

namespace MdvApp.Pages;

/// <summary>
/// Shared helpers for the placeholder shell: cross-page navigation and a stub
/// dialog for actions whose engine (MdvCore) is not implemented yet.
/// </summary>
internal static class AppActions
{
    public static void Navigate(Type pageType) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(pageType);

    public static void NotImplemented() =>
        MessageBox.Show(
            "The MDV format engine (MdvCore) is not implemented yet.\n\n" +
            "This is a UI shell — wire this action up once load/save/import lands.",
            "Coming soon",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
