using System.Collections.Generic;
using System.Text;

namespace MdvApp.Models;

/// <summary>Formats raw bytes into classic hex-dump rows and a printable text view.</summary>
public static class HexDump
{
    private const int BytesPerRow = 16;

    /// <summary>One formatted row per 16 bytes: "OFFSET  HH HH .. HH  ascii".</summary>
    public static IReadOnlyList<string> ToRows(byte[] data)
    {
        var rows = new List<string>((data.Length / BytesPerRow) + 1);
        var sb = new StringBuilder(80);

        for (int offset = 0; offset < data.Length; offset += BytesPerRow)
        {
            sb.Clear();
            sb.Append(offset.ToString("X8")).Append("  ");

            for (int i = 0; i < BytesPerRow; i++)
            {
                if (offset + i < data.Length)
                    sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else
                    sb.Append("   ");
                if (i == 7)
                    sb.Append(' ');
            }

            sb.Append(' ');
            for (int i = 0; i < BytesPerRow && offset + i < data.Length; i++)
                sb.Append(Printable(data[offset + i]));

            rows.Add(sb.ToString());
        }

        return rows;
    }

    /// <summary>Decode every byte as Latin-1, showing control bytes as '.'.</summary>
    public static string ToText(byte[] data)
    {
        var sb = new StringBuilder(data.Length);
        foreach (byte b in data)
            sb.Append(b == '\r' || b == '\n' || b == '\t' ? (char)b : Printable(b));
        return sb.ToString();
    }

    private static char Printable(byte b) => b is >= 0x20 and < 0x7F ? (char)b : '.';
}
