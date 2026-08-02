using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Builds font files that no fixture on disk provides — currently a CFF-flavoured face, used
/// to prove the native backend rejects PostScript outlines instead of rendering nothing.
/// </summary>
internal static class SyntheticFonts
{
    /// <summary>
    /// Re-wraps Roboto's real metric tables in an <c>OTTO</c> (CFF) shell: the sfnt version
    /// says CFF, <c>glyf</c>/<c>loca</c> are dropped, and a stub <c>CFF </c> table takes their
    /// place. Every table <see cref="FontValidator"/> demands is still present, so the font
    /// loads cleanly and the only thing that can reject it is the missing TrueType outlines.
    /// </summary>
    public static byte[] CffFlavoured()
    {
        var source = TableProvider.Parse(TestFonts.RobotoRegularBytes());

        var tables = new List<(string Tag, byte[] Data)>();
        foreach (string tag in new[] { "head", "cmap", "hhea", "hmtx", "maxp", "name", "OS/2", "post" })
            tables.Add((tag, source.GetTable(tag).ToArray()));

        // A stub is enough: nothing in the load path parses CFF charstrings yet.
        tables.Add(("CFF ", new byte[] { 0x01, 0x00, 0x04, 0x04 }));
        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        return BuildSfnt(0x4F54544F, tables); // 'OTTO'
    }

    private static byte[] BuildSfnt(uint sfntVersion, List<(string Tag, byte[] Data)> tables)
    {
        int directoryEnd = 12 + (tables.Count * 16);
        int total = directoryEnd;
        foreach (var (_, data) in tables)
            total += Align4(data.Length);

        var font = new byte[total];
        var span = font.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)tables.Count);
        // searchRange / entrySelector / rangeShift are derived fields the parser skips.

        int recordOffset = 12;
        int dataOffset = directoryEnd;
        foreach (var (tag, data) in tables)
        {
            for (int i = 0; i < 4; i++)
                font[recordOffset + i] = (byte)tag[i];

            BinaryPrimitives.WriteUInt32BigEndian(span[(recordOffset + 4)..], 0u); // checksum, unverified
            BinaryPrimitives.WriteUInt32BigEndian(span[(recordOffset + 8)..], (uint)dataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(span[(recordOffset + 12)..], (uint)data.Length);

            data.CopyTo(span[dataOffset..]);

            recordOffset += 16;
            dataOffset += Align4(data.Length);
        }

        return font;
    }

    private static int Align4(int length) => (length + 3) & ~3;
}
