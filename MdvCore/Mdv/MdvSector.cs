namespace MdvCore.Mdv;

/// <summary>State of a physical sector, derived from the sector-0 allocation map.</summary>
public enum MdvSectorState
{
    /// <summary>Holds the directory/allocation map itself (file-number sentinel 0xF8).</summary>
    Map,

    /// <summary>Allocated to a file (directory file 0 or any data/exec file).</summary>
    Used,

    /// <summary>Unallocated and available (sentinel 0xFD).</summary>
    Free,

    /// <summary>Marked unusable (sentinel 0xFF).</summary>
    Damaged,
}

/// <summary>One entry of the 255-sector allocation map.</summary>
public sealed record MdvSectorInfo(int Index, MdvSectorState State, byte FileNumber, byte FileBlock);
