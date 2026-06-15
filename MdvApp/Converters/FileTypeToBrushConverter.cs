using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MdvCore.Mdv;

namespace MdvApp.Converters;

/// <summary>Executable files render in light green; everything else in white.</summary>
public sealed class FileTypeToBrushConverter : IValueConverter
{
    private static readonly Brush Executable = Frozen(0x8F, 0xE3, 0x8F);
    private static readonly Brush Default = Brushes.White;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is MdvFileType.Executable ? Executable : Default;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
