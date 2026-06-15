using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace MdvApp.Converters;

/// <summary>Maps a file's executable flag to the Fluent icon shown in the directory list.</summary>
public sealed class FileKindToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.AppGeneric24 : SymbolRegular.DocumentText24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
