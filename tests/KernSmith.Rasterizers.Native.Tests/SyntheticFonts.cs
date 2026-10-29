using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Builds font files that no fixture on disk provides: CFF-flavoured faces around a hand-built
/// (or deliberately broken) <c>CFF </c>/<c>CFF2</c> table, and fixture fonts with individual
/// tables added, swapped out or removed.
/// </summary>
internal static class SyntheticFonts
{
    /// <summary>
    /// Re-wraps Roboto's real metric tables in an <c>OTTO</c> (CFF) shell: the sfnt version
    /// says CFF, <c>glyf</c>/<c>loca</c> are dropped, and a stub <c>CFF </c> table — a bare
    /// header, which no decoder can use — takes their place. Every table
    /// <see cref="FontValidator"/> demands is still present, so the face itself loads cleanly.
    /// </summary>
    public static byte[] CffFlavoured() => CffFlavoured([0x01, 0x00, 0x04, 0x04]);

    /// <summary>
    /// As <see cref="CffFlavoured()"/>, around the supplied outline table. Roboto's <c>hmtx</c>
    /// and <c>head</c> (unitsPerEm 2048) come along, so advances are Roboto's.
    /// </summary>
    public static byte[] CffFlavoured(byte[] outlineTable, string tag = "CFF ")
    {
        var source = TableProvider.Parse(TestFonts.RobotoRegularBytes());

        var tables = new List<(string Tag, byte[] Data)>();
        foreach (string required in new[] { "head", "cmap", "hhea", "hmtx", "maxp", "name", "OS/2", "post" })
            tables.Add((required, source.GetTable(required).ToArray()));

        tables.Add((tag, outlineTable));
        tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        return BuildSfnt(0x4F54544F, tables); // 'OTTO'
    }

    /// <summary>
    /// Roboto with extra tables added (or replaced) — e.g. a hand-built <c>COLR</c>/<c>CPAL</c>
    /// pair, so color layering can be tested on glyph shapes the other tests already know.
    /// </summary>
    public static byte[] RobotoWith(params (string Tag, byte[] Data)[] extraTables) =>
        WithTables(TestFonts.RobotoRegularBytes(), extraTables.ToDictionary(t => t.Tag, t => (byte[]?)t.Data));

    /// <summary>
    /// Rebuilds a font with some tables replaced or removed (a null value drops the table); every
    /// other table, and the sfnt flavour (TrueType or CFF), is kept.
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
        return BuildSfnt(source.IsCff ? 0x4F54544Fu : 0x00010000u, tables); // keep 'OTTO' for a CFF font
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

    /// <summary>
    /// Rebuilds a TrueType font with one glyph's <c>hmtx</c> advance width moved by
    /// <paramref name="delta"/> font units; the glyph must have a full (advance, lsb) pair.
    /// </summary>
    public static byte[] WithAdvanceWidthShifted(byte[] font, int glyphIndex, int delta)
    {
        var face = NativeFontFace.Load(font);
        var hmtx = face.Tables.GetTable("hmtx").ToArray();
        if (glyphIndex >= face.Hhea.NumberOfHMetrics)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex), glyphIndex, "The glyph shares the last advance width.");

        int offset = glyphIndex * 4;
        ushort advance = BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(offset));
        BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(offset), checked((ushort)(advance + delta)));

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
