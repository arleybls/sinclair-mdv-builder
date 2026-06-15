using System.Windows;
using System.Windows.Media;
using MdvCore.Mdv;

namespace MdvApp.Models;

/// <summary>A label/value pair shown on the Media Info page.</summary>
public sealed record InfoRow(string Label, string Value);

/// <summary>A sector-map cell: index, fill colour for its state, and highlight border.</summary>
public sealed class SectorCellView
{
    public int Index { get; }
    public Brush Fill { get; }
    public Brush BorderBrush { get; }
    public Thickness BorderThickness { get; }

    public SectorCellView(MdvSectorInfo sector, bool isHighlighted = false)
    {
        Index = sector.Index;
        Fill = sector.State switch
        {
            MdvSectorState.Map => MapBrush,
            MdvSectorState.Used => UsedBrush,
            MdvSectorState.Damaged => DamagedBrush,
            _ => FreeBrush,
        };
        BorderBrush = isHighlighted ? HighlightBrush : Brushes.Transparent;
        BorderThickness = new Thickness(isHighlighted ? 2 : 0);
    }

    public static readonly Brush MapBrush = Frozen(0x4C, 0x9A, 0xFF);
    public static readonly Brush UsedBrush = Frozen(0x3A, 0x7A, 0x3A);
    public static readonly Brush FreeBrush = Frozen(0x3A, 0x3A, 0x3A);
    public static readonly Brush DamagedBrush = Frozen(0xB0, 0x3A, 0x3A);
    public static readonly Brush HighlightBrush = Frozen(0xF2, 0xE6, 0x4B);

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
