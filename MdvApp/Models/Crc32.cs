namespace MdvApp.Models;

/// <summary>Standard CRC-32 (IEEE 802.3, polynomial 0xEDB88320) over a byte buffer.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
