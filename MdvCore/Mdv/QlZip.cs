using System.Text;

namespace MdvCore.Mdv;

/// <summary>
/// Codec for the QDOS/SMS ZIP extra field (the "qlzip" convention) that carries a file's 64-byte QL
/// header inside a ZIP entry, so QL file type and data-space survive a round-trip through a ZIP.
/// Pure byte logic with no ZIP-library dependency — the caller owns reading/writing the archive.
/// </summary>
public static class QlZip
{
    /// <summary>The QDOS extra-field tag (little-endian in the ZIP record).</summary>
    public const ushort QdosExtraFieldId = 0xFB4A;

    private const int QlHeaderSize = 64;
    private const int QlExtraPayloadSize = 8 + QlHeaderSize; // LongID(4) + ExtraID(4) + qdirect(64)

    /// <summary>Build the QDOS (0xFB4A) ZIP extra-field block wrapping a 64-byte QL header.</summary>
    public static byte[] BuildQlExtraField(byte[] qlHeader)
    {
        ArgumentNullException.ThrowIfNull(qlHeader);

        var field = new byte[4 + QlExtraPayloadSize];
        field[0] = (byte)(QdosExtraFieldId & 0xFF);   // tag (little-endian)
        field[1] = (byte)(QdosExtraFieldId >> 8);
        field[2] = (byte)(QlExtraPayloadSize & 0xFF); // data size (little-endian)
        field[3] = (byte)(QlExtraPayloadSize >> 8);
        Encoding.ASCII.GetBytes("QZHD").CopyTo(field, 4);  // LongID
        Encoding.ASCII.GetBytes("QDOS").CopyTo(field, 8);  // ExtraID
        Array.Copy(qlHeader, 0, field, 12, Math.Min(qlHeader.Length, QlHeaderSize));
        return field;
    }

    /// <summary>
    /// Scan a ZIP extra-data block for the QDOS field and read the QL type / data-space.
    /// Returns (0, 0) when there is no usable QDOS field (the file imports as plain data).
    /// </summary>
    public static (byte TypeCode, uint DataSpace) ReadQlExtraField(byte[]? extra)
    {
        if (extra == null)
            return (0, 0);

        int i = 0;
        while (i + 4 <= extra.Length)
        {
            int id = extra[i] | (extra[i + 1] << 8);
            int size = extra[i + 2] | (extra[i + 3] << 8);
            int dataStart = i + 4;
            if (dataStart + size > extra.Length)
                break;

            // The QL header (qdirect) follows the 8-byte LongID/ExtraID prefix.
            if (id == QdosExtraFieldId && size >= QlExtraPayloadSize)
                return MdvCartridge.ReadQlFileHeader(extra.AsSpan(dataStart + 8, QlHeaderSize));

            i = dataStart + size;
        }
        return (0, 0);
    }
}
