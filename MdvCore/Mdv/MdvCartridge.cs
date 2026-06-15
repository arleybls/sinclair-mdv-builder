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

    public string MediumName { get; }
    public ushort MediumId { get; }
    public IReadOnlyList<MdvFileEntry> Files { get; }
    public IReadOnlyList<MdvSectorInfo> Sectors { get; }

    public int FreeSectorCount { get; }
    public int DamagedSectorCount { get; }
    public int UsedSectorCount => SectorCount - FreeSectorCount - DamagedSectorCount;

    private MdvCartridge(
        string mediumName,
        ushort mediumId,
        IReadOnlyList<MdvFileEntry> files,
        IReadOnlyList<MdvSectorInfo> sectors)
    {
        MediumName = mediumName;
        MediumId = mediumId;
        Files = files;
        Sectors = sectors;
        FreeSectorCount = sectors.Count(s => s.State == MdvSectorState.Free);
        DamagedSectorCount = sectors.Count(s => s.State == MdvSectorState.Damaged);
    }

    /// <summary>Load and inspect an MDV image from disk.</summary>
    public static MdvCartridge Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("No cartridge path supplied.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Cartridge file not found.", path);

        return LoadMdv(File.ReadAllBytes(path));
    }

    /// <summary>Load and inspect an MDV image from raw bytes.</summary>
    public static MdvCartridge LoadMdv(byte[] raw)
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
        return new MdvCartridge(mediumName, mediumId, files, sectors);
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
            bool isExecutable = dir[offset + 5] == 0x01;
            uint dataSpace = ReadBe32(dir, offset + 6);
            int nameLength = Math.Min(ReadBe16(dir, offset + 14), (ushort)36);
            string name = Encoding.ASCII.GetString(dir, offset + 16, nameLength);

            int blockCount = 0;
            for (int s = 0; s < SectorCount; s++)
                if (fileNumberOf[s] == fileNumber)
                    blockCount++;

            long dataLength = fileLength >= FileHeaderSize ? fileLength - FileHeaderSize : 0;
            files.Add(new MdvFileEntry(fileNumber, name, isExecutable, dataLength, dataSpace, blockCount));
        }

        return files;
    }

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
