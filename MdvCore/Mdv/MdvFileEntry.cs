namespace MdvCore.Mdv;

/// <summary>
/// A file listed in a cartridge directory. <see cref="DataLength"/> is the file's
/// actual content size (the on-disk FileLength minus the 64-byte file header).
/// </summary>
public sealed record MdvFileEntry(
    byte FileNumber,
    string Name,
    bool IsExecutable,
    long DataLength,
    uint DataSpace,
    int BlockCount)
{
    /// <summary>Human-readable file type for display.</summary>
    public string TypeLabel => IsExecutable ? "Executable" : "Data";
}
