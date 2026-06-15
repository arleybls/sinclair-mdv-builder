using System.Text;

namespace MdvCore.Mdv;

/// <summary>
/// A loaded Sinclair QL microdrive image: its medium identity, directory listing
/// and sector allocation map. Read-only inspection model produced by <see cref="Load"/>.
/// </summary>
public sealed class MdvCartridge
{
    public const int SectorCount = 255;
    public const int SectorSize = 686;
    public const int ImageSize = SectorCount * SectorSize; // 174,930

    // Byte offsets within a 686-byte on-disk sector.
    private const int HeaderOffset = 12;       // after the 12-byte preamble
    private const int RecordOffset = 40;       // preamble(12)+header(16)+preamble(12)
    private const int RecordDataOffset = RecordOffset + 12; // record: 2+2+8 then 512 data => +12
    private const int SectorDataSize = 512;

    private const int FileHeaderSize = 64;

    // File-number sentinels stored in the allocation map.
    private const byte FreeSector = 0xFD;
    private const byte DamagedSector = 0xFF;
    private const byte MapFile = 0xF8;
    private const byte DirectoryFile = 0x00;

    // The raw 174,930-byte image this cartridge was loaded from. Kept so the
    // cartridge can be written back byte-for-byte (Save As) before an in-place
    // write engine exists.
    private readonly byte[] _image;

    // Parsed sector state, kept so a file's bytes can be reassembled on demand.
    private readonly byte[]?[] _dataBySector;
    private readonly byte[] _fileNumberOf;
    private readonly byte[] _fileBlockOf;

    public string MediumName { get; }
    public ushort MediumId { get; }
    public IReadOnlyList<MdvFileEntry> Files { get; }
    public IReadOnlyList<MdvSectorInfo> Sectors { get; }

    /// <summary>Path the image was loaded from, if any (null when loaded from bytes).</summary>
    public string? SourcePath { get; }

    public int FreeSectorCount { get; }
    public int DamagedSectorCount { get; }
    public int UsedSectorCount => SectorCount - FreeSectorCount - DamagedSectorCount;

    private MdvCartridge(
        byte[] image,
        string? sourcePath,
        string mediumName,
        ushort mediumId,
        IReadOnlyList<MdvFileEntry> files,
        IReadOnlyList<MdvSectorInfo> sectors,
        byte[]?[] dataBySector,
        byte[] fileNumberOf,
        byte[] fileBlockOf)
    {
        _image = image;
        SourcePath = sourcePath;
        MediumName = mediumName;
        MediumId = mediumId;
        Files = files;
        Sectors = sectors;
        _dataBySector = dataBySector;
        _fileNumberOf = fileNumberOf;
        _fileBlockOf = fileBlockOf;
        FreeSectorCount = sectors.Count(s => s.State == MdvSectorState.Free);
        DamagedSectorCount = sectors.Count(s => s.State == MdvSectorState.Damaged);
    }

    /// <summary>Sectors available for file/directory data: 1..254 minus damaged ones.</summary>
    public int AvailableSectors => (SectorCount - 1) - DamagedSectorCount;

    /// <summary>Find a listed file by name (case-insensitive), or null.</summary>
    public MdvFileEntry? FindFile(string name)
    {
        string clean = CleanFileName(name);
        return Files.FirstOrDefault(f => string.Equals(f.Name, clean, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether importing <paramref name="contentLength"/> bytes as <paramref name="name"/> fits,
    /// reporting the sectors needed and available for the resulting file set.
    /// </summary>
    public bool WouldFit(long contentLength, string name, bool overwriteExisting, out int needed, out int available)
    {
        string clean = CleanFileName(name);

        var lengths = new List<long>();
        foreach (var f in Files)
        {
            if (overwriteExisting && string.Equals(f.Name, clean, StringComparison.OrdinalIgnoreCase))
                continue;
            lengths.Add(f.DataLength);
        }
        lengths.Add(contentLength);

        int directoryBytes = (lengths.Count + 1) * FileHeaderSize;
        int blocks = Blocks(directoryBytes);
        foreach (long len in lengths)
            blocks += Blocks(len + FileHeaderSize);

        needed = blocks;
        available = AvailableSectors;
        return needed <= available;
    }

    /// <summary>
    /// Return a new cartridge with <paramref name="content"/> imported as <paramref name="name"/>.
    /// Replaces an existing same-named file when <paramref name="overwrite"/> is set, otherwise adds it.
    /// Throws <see cref="MdvInsufficientSpaceException"/> if it does not fit.
    /// </summary>
    public MdvCartridge ImportFile(string name, byte[] content, byte typeCode = 0, uint dataSpace = 0, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        string clean = CleanFileName(name);

        var files = new List<(byte[] Header, byte[] Content)>();
        bool replaced = false;

        foreach (var f in Files)
        {
            if (overwrite && string.Equals(f.Name, clean, StringComparison.OrdinalIgnoreCase))
            {
                files.Add((MdvImageBuilder.BuildFileHeader(
                    clean, typeCode, content.Length, dataSpace,
                    f.FileAccess, f.ExtraInfo, f.UpdateDate, f.ReferenceDate, f.BackupDate), content));
                replaced = true;
            }
            else
            {
                files.Add((MdvImageBuilder.BuildFileHeader(
                    f.Name, f.TypeCode, f.DataLength, f.DataSpace,
                    f.FileAccess, f.ExtraInfo, f.UpdateDate, f.ReferenceDate, f.BackupDate), ReadFileData(f)));
            }
        }

        if (!replaced)
            files.Add((MdvImageBuilder.BuildFileHeader(clean, typeCode, content.Length, dataSpace, 0, 0, 0, 0, 0), content));

        var damaged = Sectors.Where(s => s.State == MdvSectorState.Damaged).Select(s => s.Index).ToList();
        byte[] image = MdvImageBuilder.Build(MediumName, MediumId, damaged, files);
        return LoadMdv(image, SourcePath);
    }

    /// <summary>
    /// Return a new cartridge with the named file removed. If no such file exists the current
    /// cartridge is returned unchanged.
    /// </summary>
    public MdvCartridge DeleteFile(string name)
    {
        string clean = CleanFileName(name);

        var files = new List<(byte[] Header, byte[] Content)>();
        bool removed = false;
        foreach (var f in Files)
        {
            if (!removed && string.Equals(f.Name, clean, StringComparison.OrdinalIgnoreCase))
            {
                removed = true;
                continue;
            }

            files.Add((MdvImageBuilder.BuildFileHeader(
                f.Name, f.TypeCode, f.DataLength, f.DataSpace,
                f.FileAccess, f.ExtraInfo, f.UpdateDate, f.ReferenceDate, f.BackupDate), ReadFileData(f)));
        }

        if (!removed)
            return this;

        var damaged = Sectors.Where(s => s.State == MdvSectorState.Damaged).Select(s => s.Index).ToList();
        byte[] image = MdvImageBuilder.Build(MediumName, MediumId, damaged, files);
        return LoadMdv(image, SourcePath);
    }

    /// <summary>
    /// Return a new cartridge with <paramref name="name"/> renamed to <paramref name="newName"/>.
    /// Returns the current cartridge unchanged if the source file is not found.
    /// </summary>
    public MdvCartridge RenameFile(string name, string newName)
    {
        string from = CleanFileName(name);
        string to = CleanFileName(newName);

        var files = new List<(byte[] Header, byte[] Content)>();
        bool renamed = false;
        foreach (var f in Files)
        {
            bool isTarget = !renamed && string.Equals(f.Name, from, StringComparison.OrdinalIgnoreCase);
            if (isTarget)
                renamed = true;

            files.Add((MdvImageBuilder.BuildFileHeader(
                isTarget ? to : f.Name, f.TypeCode, f.DataLength, f.DataSpace,
                f.FileAccess, f.ExtraInfo, f.UpdateDate, f.ReferenceDate, f.BackupDate), ReadFileData(f)));
        }

        if (!renamed)
            return this;

        var damaged = Sectors.Where(s => s.State == MdvSectorState.Damaged).Select(s => s.Index).ToList();
        byte[] image = MdvImageBuilder.Build(MediumName, MediumId, damaged, files);
        return LoadMdv(image, SourcePath);
    }

    /// <summary>
    /// Return a new cartridge with the named file's type (and data space) changed.
    /// Returns the current cartridge unchanged if the file is not found.
    /// </summary>
    public MdvCartridge SetFileType(string name, byte typeCode, uint dataSpace)
    {
        string target = CleanFileName(name);

        var files = new List<(byte[] Header, byte[] Content)>();
        bool changed = false;
        foreach (var f in Files)
        {
            bool isTarget = !changed && string.Equals(f.Name, target, StringComparison.OrdinalIgnoreCase);
            if (isTarget)
                changed = true;

            files.Add((MdvImageBuilder.BuildFileHeader(
                f.Name,
                isTarget ? typeCode : f.TypeCode,
                f.DataLength,
                isTarget ? dataSpace : f.DataSpace,
                f.FileAccess, f.ExtraInfo, f.UpdateDate, f.ReferenceDate, f.BackupDate), ReadFileData(f)));
        }

        if (!changed)
            return this;

        var damaged = Sectors.Where(s => s.State == MdvSectorState.Damaged).Select(s => s.Index).ToList();
        byte[] image = MdvImageBuilder.Build(MediumName, MediumId, damaged, files);
        return LoadMdv(image, SourcePath);
    }

    /// <summary>Normalise a host filename into a QL file name (dots become underscores, max 36 chars).</summary>
    public static string CleanFileName(string name)
    {
        string clean = (name ?? string.Empty).Replace('.', '_');
        return clean.Length > 36 ? clean[..36] : clean;
    }

    private static int Blocks(long byteCount) => (int)((byteCount + 511) / 512);

    /// <summary>The 512-byte data payload of a physical sector, or null if not present.</summary>
    public byte[]? GetSectorData(int sectorNumber)
    {
        if (sectorNumber < 0 || sectorNumber >= _dataBySector.Length)
            return null;
        byte[]? data = _dataBySector[sectorNumber];
        return data == null ? null : (byte[])data.Clone();
    }

    /// <summary>Reassemble and return a file's content bytes (without its 64-byte file header).</summary>
    public byte[] ReadFileData(MdvFileEntry file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ReadFileData(file.FileNumber, file.DataLength);
    }

    /// <summary>Reassemble and return <paramref name="dataLength"/> content bytes of a file.</summary>
    public byte[] ReadFileData(byte fileNumber, long dataLength)
    {
        byte[] raw = Reassemble(fileNumber);

        // The file's own 64-byte header sits at the start of the first block.
        long available = Math.Max(0, raw.Length - FileHeaderSize);
        int length = (int)Math.Clamp(dataLength, 0, available);

        var content = new byte[length];
        Array.Copy(raw, FileHeaderSize, content, 0, length);
        return content;
    }

    /// <summary>The raw MDV image bytes (a copy).</summary>
    public byte[] ToBytes() => (byte[])_image.Clone();

    /// <summary>Write the image to <paramref name="path"/> as a native .MDV file.</summary>
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("No output path supplied.", nameof(path));
        File.WriteAllBytes(path, _image);
    }

    /// <summary>Load and inspect an MDV image from disk.</summary>
    public static MdvCartridge Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("No cartridge path supplied.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Cartridge file not found.", path);

        return LoadMdv(File.ReadAllBytes(path), path);
    }

    /// <summary>Load and inspect an MDV image from raw bytes.</summary>
    public static MdvCartridge LoadMdv(byte[] raw, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length != ImageSize)
            throw new InvalidDataException(
                $"Not a native MDV image: expected {ImageSize:N0} bytes but got {raw.Length:N0}.");

        // The 512-byte data payload of each sector, keyed by the sector number
        // declared in its header (sectors are not stored in numeric order on disk).
        var dataBySector = new byte[256][];
        byte[]? sectorZeroData = null;
        string mediumName = string.Empty;
        ushort mediumId = 0;

        for (int i = 0; i < SectorCount; i++)
        {
            int baseOffset = i * SectorSize;
            byte flag = raw[baseOffset + HeaderOffset];
            byte sectorNumber = raw[baseOffset + HeaderOffset + 1];

            if (flag != 0xFF)
                continue; // not a valid written sector

            byte[] data = new byte[SectorDataSize];
            Array.Copy(raw, baseOffset + RecordDataOffset, data, 0, SectorDataSize);
            dataBySector[sectorNumber] = data;

            if (sectorNumber == 0)
            {
                sectorZeroData = data;
                mediumName = Encoding.ASCII
                    .GetString(raw, baseOffset + HeaderOffset + 2, 10)
                    .TrimEnd(' ', '\0');
                mediumId = ReadBe16(raw, baseOffset + HeaderOffset + 12);
            }
        }

        if (sectorZeroData == null)
            throw new InvalidDataException("Directory/map sector (sector 0) not found.");

        // Allocation map: two bytes per sector in sector 0's payload.
        var sectors = new MdvSectorInfo[SectorCount];
        var fileNumberOf = new byte[SectorCount];
        var fileBlockOf = new byte[SectorCount];

        for (int s = 0; s < SectorCount; s++)
        {
            byte fileNumber = sectorZeroData[s * 2];
            byte fileBlock = sectorZeroData[s * 2 + 1];
            fileNumberOf[s] = fileNumber;
            fileBlockOf[s] = fileBlock;

            MdvSectorState state = fileNumber switch
            {
                FreeSector => MdvSectorState.Free,
                DamagedSector => MdvSectorState.Damaged,
                MapFile => MdvSectorState.Map,
                _ => MdvSectorState.Used,
            };
            sectors[s] = new MdvSectorInfo(s, state, fileNumber, fileBlock);
        }

        var files = ReadDirectory(dataBySector, fileNumberOf, fileBlockOf);
        return new MdvCartridge(
            raw, sourcePath, mediumName, mediumId, files, sectors,
            dataBySector, fileNumberOf, fileBlockOf);
    }

    /// <summary>
    /// Create a new, empty cartridge (no files) with the given medium name. Sector 254 is
    /// reserved as damaged, mirroring a freshly formatted cartridge.
    /// </summary>
    public static MdvCartridge CreateEmpty(string mediumName, ushort? mediumId = null)
    {
        ushort id = mediumId ?? (ushort)Random.Shared.Next(0, 65536);
        var damaged = new[] { SectorCount - 1 }; // 254
        byte[] image = MdvImageBuilder.Build(
            mediumName ?? string.Empty, id, damaged,
            Array.Empty<(byte[] Header, byte[] Content)>());
        return LoadMdv(image, sourcePath: null);
    }

    private static List<MdvFileEntry> ReadDirectory(
        byte[]?[] dataBySector, byte[] fileNumberOf, byte[] fileBlockOf)
    {
        var files = new List<MdvFileEntry>();

        // The directory is stored as file number 0. Its content starts with the
        // directory's own 64-byte header, then one 64-byte header per file.
        byte[] dir = Reassemble(DirectoryFile, dataBySector, fileNumberOf, fileBlockOf);
        if (dir.Length < FileHeaderSize)
            return files;

        long directoryLength = ReadBe32(dir, 0);
        long entriesLength = directoryLength - FileHeaderSize;

        // An image with no standard QL directory (blank, or a patched/raw game image)
        // reassembles to an empty sector, so its declared length is implausible. Treat
        // it as "no files" and still surface the sector map, rather than failing the load.
        const long MaxDirectory = (long)SectorCount * SectorDataSize;
        bool plausible = directoryLength >= FileHeaderSize
            && directoryLength <= MaxDirectory
            && entriesLength % FileHeaderSize == 0
            && FileHeaderSize + entriesLength <= dir.Length;
        if (!plausible || entriesLength == 0)
            return files;

        int fileCount = (int)(entriesLength / FileHeaderSize);
        for (int k = 0; k < fileCount; k++)
        {
            byte fileNumber = (byte)(k + 1);
            int offset = FileHeaderSize + k * FileHeaderSize;

            uint fileLength = ReadBe32(dir, offset + 0);
            byte fileAccess = dir[offset + 4];
            byte typeCode = dir[offset + 5];
            uint dataSpace = ReadBe32(dir, offset + 6);
            uint extraInfo = ReadBe32(dir, offset + 10);
            int nameLength = Math.Min(ReadBe16(dir, offset + 14), (ushort)36);
            string name = Encoding.ASCII.GetString(dir, offset + 16, nameLength);
            uint updateDate = ReadBe32(dir, offset + 52);
            uint referenceDate = ReadBe32(dir, offset + 56);
            uint backupDate = ReadBe32(dir, offset + 60);

            int blockCount = 0;
            for (int s = 0; s < SectorCount; s++)
                if (fileNumberOf[s] == fileNumber)
                    blockCount++;

            long dataLength = fileLength >= FileHeaderSize ? fileLength - FileHeaderSize : 0;
            files.Add(new MdvFileEntry(
                fileNumber, name, typeCode, dataLength, dataSpace, blockCount,
                fileAccess, extraInfo, updateDate, referenceDate, backupDate));
        }

        return files;
    }

    private byte[] Reassemble(byte fileNumber) =>
        Reassemble(fileNumber, _dataBySector, _fileNumberOf, _fileBlockOf);

    /// <summary>Concatenate, in block order, the 512-byte payloads of every sector owning a file.</summary>
    private static byte[] Reassemble(
        byte fileNumber, byte[]?[] dataBySector, byte[] fileNumberOf, byte[] fileBlockOf)
    {
        var blocks = Enumerable.Range(0, SectorCount)
            .Where(s => fileNumberOf[s] == fileNumber)
            .OrderBy(s => fileBlockOf[s])
            .ToList();

        var buffer = new byte[blocks.Count * SectorDataSize];
        int pos = 0;
        foreach (int sectorNumber in blocks)
        {
            byte[]? data = dataBySector[sectorNumber];
            if (data == null)
                throw new InvalidDataException($"Sector {sectorNumber} referenced by the map is missing.");
            Array.Copy(data, 0, buffer, pos, SectorDataSize);
            pos += SectorDataSize;
        }
        return buffer;
    }

    private static ushort ReadBe16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);

    private static uint ReadBe32(byte[] b, int o) =>
        (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
}
