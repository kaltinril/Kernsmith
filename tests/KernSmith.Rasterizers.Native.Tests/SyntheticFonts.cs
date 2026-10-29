using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Builds font files that no fixture on disk provides — a CFF-flavoured face, used to prove the
/// native backend rejects PostScript outlines instead of rendering nothing, and fixture fonts
/// with individual tables swapped out.
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

    /// <summary>
    /// Rebuilds a TrueType font with some tables replaced or removed (a null value drops the
    /// table); every other table is copied verbatim.
    /// </summary>
    public static byte[] WithTables(byte[] font, IReadOnlyDictionary<string, byte[]?> replacements)
    {
        var source = TableProvider.Parse(font);

        var tables = new List<(string Tag, byte[] Data)>();
        foreach (string tag in source.TableTags)
        {
            if (!replacements.ContainsKey(tag))
                tables.Add((tag, source.GetTable(tag).ToArray()));
        }

        foreach (var (tag, data) in replacements)
        {
            if (data is not null)
                tables.Add((tag, data));
        }

        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        return BuildSfnt(0x00010000, tables);
    }

    /// <summary>
    /// Rebuilds a TrueType font with one glyph's <c>hmtx</c> left side bearing moved by
    /// <paramref name="delta"/> font units; the outline (and its <c>glyf</c> xMin) is untouched,
    /// so the bearing and the outline disagree by exactly that much.
    /// </summary>
    public static byte[] WithLeftSideBearingShifted(byte[] font, int glyphIndex, int delta)
    {
        var face = NativeFontFace.Load(font);
        var hmtx = face.Tables.GetTable("hmtx").ToArray();
        int fullMetrics = face.Hhea.NumberOfHMetrics;

        // (advance, lsb) pairs first, then bare bearings for the monospaced tail.
        int offset = glyphIndex < fullMetrics
            ? (glyphIndex * 4) + 2
            : (fullMetrics * 4) + ((glyphIndex - fullMetrics) * 2);

        short lsb = BinaryPrimitives.ReadInt16BigEndian(hmtx.AsSpan(offset));
        BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(offset), checked((short)(lsb + delta)));

        return WithTables(font, new Dictionary<string, byte[]?> { ["hmtx"] = hmtx });
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
