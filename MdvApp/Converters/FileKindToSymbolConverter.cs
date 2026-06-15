using System;
using System.Globalization;
using System.Windows.Data;
using MdvCore.Mdv;
using Wpf.Ui.Controls;

namespace MdvApp.Converters;

/// <summary>Maps a file's type to the Fluent icon shown in the directory list and detail pane.</summary>
public sealed class FileKindToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is MdvFileType type
            ? type switch
            {
                MdvFileType.Executable => SymbolRegular.AppGeneric24,
                MdvFileType.Relocatable => SymbolRegular.Code24,
                MdvFileType.Directory => SymbolRegular.Folder24,
                _ => SymbolRegular.DocumentText24,
            }
            : SymbolRegular.DocumentText24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
