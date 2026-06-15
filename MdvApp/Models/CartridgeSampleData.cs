using System.Collections.Generic;
using System.Windows.Media;

namespace MdvApp.Models;

// PLACEHOLDER UI DATA ONLY.
// These types exist so the shell renders something meaningful before the real
// format engine lands in MdvCore. Once MdvCore can load/inspect an image, the
// pages should bind to MdvCore types and this file should be deleted.

public enum MdvFileKind
{
    Data,
    Executable,
}

public sealed record MdvFileRow(string Name, MdvFileKind Kind, int LengthBytes, int Blocks)
{
    public string KindLabel => Kind == MdvFileKind.Executable ? "Executable" : "Data";
}

public enum SectorState
{
    Map,
    Used,
    Free,
    Damaged,
}

public sealed record SectorCell(int Index, SectorState State)
{
    public Brush Fill => State switch
    {
        SectorState.Map => new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
        SectorState.Used => new SolidColorBrush(Color.FromRgb(0x3A, 0x7A, 0x3A)),
        SectorState.Damaged => new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x3A)),
        _ => new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
    };
}

public sealed record InfoRow(string Label, string Value);

public static class CartridgeSampleData
{
    public const string MediumName = "DEMO_CART (sample)";

    public static IReadOnlyList<MdvFileRow> Files { get; } = new[]
    {
        new MdvFileRow("BOOT",        MdvFileKind.Data,       142,  1),
        new MdvFileRow("ABACUS_exe",  MdvFileKind.Executable, 38912, 76),
        new MdvFileRow("PARAMS_dat",  MdvFileKind.Data,       2048,  4),
        new MdvFileRow("README_txt",  MdvFileKind.Data,       917,   2),
        new MdvFileRow("GAME_bas",    MdvFileKind.Executable, 12544, 25),
    };

    public static IReadOnlyList<SectorCell> Sectors { get; } = BuildSectors();

    public static IReadOnlyList<InfoRow> MediaInfo { get; } = new[]
    {
        new InfoRow("Format",        "MDV (native QL microdrive)"),
        new InfoRow("Medium name",   MediumName),
        new InfoRow("Image size",    "174,930 bytes"),
        new InfoRow("Sector size",   "686 bytes"),
        new InfoRow("Sector count",  "255 (0–254)"),
        new InfoRow("Used sectors",  "108"),
        new InfoRow("Free sectors",  "145"),
        new InfoRow("Damaged",       "1"),
        new InfoRow("Files",         "5"),
    };

    private static SectorCell[] BuildSectors()
    {
        var cells = new SectorCell[255];
        for (int i = 0; i < 255; i++)
        {
            SectorState state = i switch
            {
                0 => SectorState.Map,
                113 => SectorState.Damaged,
                < 108 => SectorState.Used,
                _ => SectorState.Free,
            };
            cells[i] = new SectorCell(i, state);
        }
        return cells;
    }
}
