namespace MdvCore.Mdv;

/// <summary>Native QL file types, as stored in the file header's type byte.</summary>
public enum MdvFileType
{
    /// <summary>Type 0: data, text, documents, and SuperBASIC/SBASIC programs.</summary>
    Data,

    /// <summary>Type 1: compiled machine-code executables (EXEC/EX).</summary>
    Executable,

    /// <summary>Type 2: relocatable object files (SROFF).</summary>
    Relocatable,

    /// <summary>Type 255: internal directory catalogue files.</summary>
    Directory,

    /// <summary>Any other / unrecognised type code.</summary>
    Other,
}

/// <summary>
/// A file listed in a cartridge directory. <see cref="DataLength"/> is the file's
/// actual content size (the on-disk FileLength minus the 64-byte file header).
/// </summary>
public sealed record MdvFileEntry(
    byte FileNumber,
    string Name,
    byte TypeCode,
    long DataLength,
    uint DataSpace,
    int BlockCount,
    byte FileAccess,
    uint ExtraInfo,
    uint UpdateDate,
    uint ReferenceDate,
    uint BackupDate)
{
    /// <summary>Total on-disk file length including the 64-byte header.</summary>
    public long TotalLength => DataLength + 64;

    /// <summary>The decoded file type for the raw <see cref="TypeCode"/>.</summary>
    public MdvFileType Type => TypeCode switch
    {
        0 => MdvFileType.Data,
        1 => MdvFileType.Executable,
        2 => MdvFileType.Relocatable,
        255 => MdvFileType.Directory,
        _ => MdvFileType.Other,
    };

    public bool IsExecutable => Type == MdvFileType.Executable;

    /// <summary>True for any type other than plain data (used to decide hex vs text view).</summary>
    public bool IsBinary => Type != MdvFileType.Data;

    /// <summary>Whether the type label should be shown (everything except plain data).</summary>
    public bool ShowType => Type != MdvFileType.Data;

    /// <summary>Human-readable file type for display.</summary>
    public string TypeLabel => Type switch
    {
        MdvFileType.Data => "Data",
        MdvFileType.Executable => "Executable",
        MdvFileType.Relocatable => "Relocatable",
        MdvFileType.Directory => "Directory",
        _ => $"Type {TypeCode}",
    };
}
