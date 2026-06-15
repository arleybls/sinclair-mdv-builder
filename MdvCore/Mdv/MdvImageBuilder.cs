namespace MdvCore.Mdv;

/// <summary>Thrown when a write operation needs more sectors than the cartridge has free.</summary>
public sealed class MdvInsufficientSpaceException : Exception
{
    public int NeededSectors { get; }
    public int AvailableSectors { get; }

    public MdvInsufficientSpaceException(int needed, int available)
        : base($"Not enough space: the operation needs {needed} sectors but only {available} are available.")
    {
        NeededSectors = needed;
        AvailableSectors = available;
    }
}

/// <summary>
/// Serialises a logical cartridge (medium identity + files) into a byte-exact 174,930-byte
/// native MDV image: builds the directory file, allocates sectors, fills the sector-0
/// allocation map, computes header/record checksums, and lays sectors out on disk.
/// </summary>
internal static class MdvImageBuilder
{
    private const int SectorCount = 255;
    private const int SectorSize = 686;
    private const int ImageSize = SectorCount * SectorSize;

    private const int PreambleSize = 12;
    private const int HeaderSize = 16;
    private const int RecordSize = 612;
    private const int PadSize = 34;
    private const int SectorDataSize = 512;
    private const int FileHeaderSize = 64;

    private const byte FreeSector = 0xFD;
    private const byte DamagedSector = 0xFF;
    private const byte MapFile = 0xF8;

    /// <summary>
    /// Build the image. <paramref name="files"/> are the real files (numbers 1..N); each is its
    /// 64-byte header followed by its content. The directory file (number 0) is generated here.
    /// </summary>
    public static byte[] Build(
        string mediumName,
        ushort mediumId,
        IReadOnlyCollection<int> damagedSectors,
        IReadOnlyList<(byte[] Header, byte[] Content)> files)
    {
        // Directory file content: its own 64-byte header (carrying the total length) then one
        // 64-byte header per file.
        int directoryLength = (files.Count + 1) * FileHeaderSize;
        var directory = new byte[directoryLength];
        WriteBe32(directory, 0, (uint)directoryLength);
        for (int k = 0; k < files.Count; k++)
            Array.Copy(files[k].Header, 0, directory, (k + 1) * FileHeaderSize, FileHeaderSize);

        // Storage units in file-number order: unit 0 = directory, unit k = header + content.
        var units = new List<byte[]> { directory };
        foreach (var (header, content) in files)
        {
            var unit = new byte[header.Length + content.Length];
            Array.Copy(header, 0, unit, 0, header.Length);
            Array.Copy(content, 0, unit, header.Length, content.Length);
            units.Add(unit);
        }

        // Per-sector record state, initialised to "empty".
        var recordFileNumber = new byte[SectorCount];
        var recordFileBlock = new byte[SectorCount];
        var recordData = new byte[SectorCount][];
        var mapFileNumber = new byte[SectorCount];
        var mapFileBlock = new byte[SectorCount];

        for (int s = 0; s < SectorCount; s++)
        {
            recordFileNumber[s] = FreeSector;
            recordData[s] = EmptyPattern();
            mapFileNumber[s] = FreeSector;
        }

        mapFileNumber[0] = MapFile; // sector 0 holds the allocation map
        foreach (int d in damagedSectors)
            if (d > 0 && d < SectorCount)
                mapFileNumber[d] = DamagedSector;

        var available = new Queue<int>(
            Enumerable.Range(1, SectorCount - 1).Where(s => mapFileNumber[s] == FreeSector));

        int totalBlocks = units.Sum(u => (u.Length + SectorDataSize - 1) / SectorDataSize);
        if (totalBlocks > available.Count)
            throw new MdvInsufficientSpaceException(totalBlocks, available.Count);

        int lastAllocated = SectorCount;
        for (int unit = 0; unit < units.Count; unit++)
        {
            byte fileNumber = (byte)unit;
            byte[] content = units[unit];
            int blocks = (content.Length + SectorDataSize - 1) / SectorDataSize;

            for (int b = 0; b < blocks; b++)
            {
                int sector = available.Dequeue();
                var block = new byte[SectorDataSize];
                int start = b * SectorDataSize;
                Array.Copy(content, start, block, 0, Math.Min(SectorDataSize, content.Length - start));

                recordData[sector] = block;
                recordFileNumber[sector] = fileNumber;
                recordFileBlock[sector] = (byte)b;
                mapFileNumber[sector] = fileNumber;
                mapFileBlock[sector] = (byte)b;
                lastAllocated = sector;
            }
        }

        // Sector 0 payload: the allocation map.
        var table = new byte[SectorDataSize];
        for (int s = 0; s < SectorCount; s++)
        {
            table[s * 2] = mapFileNumber[s];
            table[s * 2 + 1] = mapFileBlock[s];
        }
        table[510] = 0x01;
        table[511] = (byte)lastAllocated;
        recordData[0] = table;
        recordFileNumber[0] = MapFile;
        recordFileBlock[0] = 0;

        // Lay out the image: sector 0 first, then the rest in descending sector-number order.
        var image = new byte[ImageSize];
        int pos = 0;
        WriteSector(image, ref pos, 0, mediumName, mediumId, recordFileNumber[0], recordFileBlock[0], recordData[0]);
        for (int s = SectorCount - 1; s >= 1; s--)
            WriteSector(image, ref pos, s, mediumName, mediumId, recordFileNumber[s], recordFileBlock[s], recordData[s]);

        return image;
    }

    /// <summary>Build a 64-byte QL file header.</summary>
    public static byte[] BuildFileHeader(
        string name, byte typeCode, long contentLength, uint dataSpace,
        byte access, uint extraInfo, uint updateDate, uint referenceDate, uint backupDate)
    {
        var h = new byte[FileHeaderSize];
        WriteBe32(h, 0, (uint)(contentLength + FileHeaderSize));
        h[4] = access;
        h[5] = typeCode;
        WriteBe32(h, 6, dataSpace);
        WriteBe32(h, 10, extraInfo);

        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        int nameLen = Math.Min(nameBytes.Length, 36);
        WriteBe16(h, 14, (ushort)nameLen);
        Array.Copy(nameBytes, 0, h, 16, nameLen);

        WriteBe32(h, 52, updateDate);
        WriteBe32(h, 56, referenceDate);
        WriteBe32(h, 60, backupDate);
        return h;
    }

    private static void WriteSector(
        byte[] image, ref int pos, int sectorNumber, string mediumName, ushort mediumId,
        byte fileNumber, byte fileBlock, byte[] data)
    {
        WritePreamble(image, ref pos);
        WriteHeader(image, ref pos, sectorNumber, mediumName, mediumId);
        WritePreamble(image, ref pos);
        WriteRecord(image, ref pos, fileNumber, fileBlock, data);
        for (int i = 0; i < PadSize; i++)
            image[pos++] = (byte)'Z';
    }

    private static void WritePreamble(byte[] image, ref int pos)
    {
        // 12 bytes; the final two are the 0xFF 0xFF sync marker.
        pos += PreambleSize - 2;
        image[pos++] = 0xFF;
        image[pos++] = 0xFF;
    }

    private static void WriteHeader(byte[] image, ref int pos, int sectorNumber, string mediumName, ushort mediumId)
    {
        int start = pos;
        image[pos++] = 0xFF; // valid-sector flag
        image[pos++] = (byte)sectorNumber;

        string padded = (mediumName ?? string.Empty).PadRight(10).Substring(0, 10);
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(padded);
        Array.Copy(nameBytes, 0, image, pos, 10);
        pos += 10;

        image[pos++] = (byte)(mediumId >> 8);   // medium id (big-endian)
        image[pos++] = (byte)(mediumId & 0xFF);

        int sum = 0;
        for (int i = 0; i < 14; i++)
            sum += image[start + i];
        WriteLe16(image, pos, (ushort)((sum + 0x0F0F) & 0xFFFF)); // checksum (little-endian field)
        pos += 2;
    }

    private static void WriteRecord(byte[] image, ref int pos, byte fileNumber, byte fileBlock, byte[] data)
    {
        int start = pos;
        image[pos++] = fileNumber;
        image[pos++] = fileBlock;
        WriteLe16(image, pos, (ushort)((fileNumber + fileBlock + 0x0F0F) & 0xFFFF)); // header checksum
        pos += 2;

        // FilePreamble[8]: the last two bytes carry the 0xFF 0xFF marker.
        pos += 6;
        image[pos++] = 0xFF;
        image[pos++] = 0xFF;

        int dataStart = pos;
        Array.Copy(data, 0, image, pos, SectorDataSize);
        pos += SectorDataSize;

        int dataSum = 0;
        for (int i = 0; i < SectorDataSize; i++)
            dataSum += image[dataStart + i];
        WriteLe16(image, pos, (ushort)((dataSum + 0x0F0F) & 0xFFFF)); // data checksum
        pos += 2;

        for (int i = 0; i < 84; i++)             // ExtraBytes: 0xAA / 0x55 alternating
            image[pos++] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

        WriteLe16(image, pos, 0x3B19);           // fixed extra-bytes checksum
        pos += 2;

        _ = start;
    }

    private static byte[] EmptyPattern()
    {
        var data = new byte[SectorDataSize];
        for (int i = 0; i < SectorDataSize; i++)
            data[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);
        return data;
    }

    private static void WriteBe16(byte[] b, int o, ushort v)
    {
        b[o] = (byte)(v >> 8);
        b[o + 1] = (byte)(v & 0xFF);
    }

    private static void WriteBe32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)(v & 0xFF);
    }

    private static void WriteLe16(byte[] b, int o, ushort v)
    {
        b[o] = (byte)(v & 0xFF);
        b[o + 1] = (byte)(v >> 8);
    }
}
