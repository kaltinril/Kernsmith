using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using KernSmith.Rasterizers.Native.Internal;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>One CBDT bitmap: the PNG and the metrics stored with it (strike pixels).</summary>
internal sealed record CbdtGlyph(int GlyphId, byte[] Png, int Width, int Height, int BearingX, int BearingY, int Advance);

/// <summary>One CBLC strike, written with one index subtable of the given formats.</summary>
internal sealed record CbdtStrike(int Ppem, int IndexFormat, int ImageFormat, CbdtGlyph[] Glyphs);

/// <summary>One sbix glyph record.</summary>
internal sealed record SbixEntry(int GlyphId, short OriginX, short OriginY, string GraphicType, byte[] Data);

/// <summary>One sbix strike.</summary>
internal sealed record SbixStrike(int Ppem, int Ppi, SbixEntry[] Entries);

/// <summary>
/// Hand-built <c>CBLC</c>/<c>CBDT</c> and <c>sbix</c> tables and the PNGs inside them, written
/// from the OpenType chapters, for fonts no fixture provides.
/// </summary>
internal static class SyntheticBitmapTables
{
    /// <summary>A straight-alpha RGBA8 PNG (color type 6, no filtering).</summary>
    public static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var raw = new byte[height * (1 + (width * 4))];
        int i = 0;
        for (int y = 0; y < height; y++)
        {
            raw[i++] = 0;
            for (int x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                raw[i++] = r;
                raw[i++] = g;
                raw[i++] = b;
                raw[i++] = a;
            }
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A PNG of one color.</summary>
    public static byte[] SolidPng(int width, int height, byte r, byte g, byte b, byte a = 255) =>
        Png(width, height, (_, _) => (r, g, b, a));

    /// <summary>Builds a <c>CBLC</c>/<c>CBDT</c> pair, one index subtable per strike.</summary>
    /// <remarks>
    /// Index formats 1 and 3 (offset arrays) and 4 (glyph/offset pairs) take image formats 17 or 18
    /// (metrics in the data) or other EBDT formats as raw bytes; 2 and 5 (constant metrics) take
    /// image format 19 and use the first glyph's metrics for all.
    /// </remarks>
    public static (byte[] Cblc, byte[] Cbdt) Cbdt(params CbdtStrike[] strikes)
    {
        var cbdt = new List<byte> { 0, 3, 0, 0 };
        var cblc = new List<byte>();
        cblc.AddRange(U16(3));
        cblc.AddRange(U16(0));
        cblc.AddRange(U32((uint)strikes.Length));
        int sizesStart = cblc.Count;
        cblc.AddRange(new byte[48 * strikes.Length]);

        for (int s = 0; s < strikes.Length; s++)
        {
            var strike = strikes[s];
            var glyphs = strike.Glyphs.OrderBy(g => g.GlyphId).ToArray();
            int first = glyphs[0].GlyphId, last = glyphs[^1].GlyphId;
            int imageDataOffset = cbdt.Count;

            // Image data, recording each glyph's offset from imageDataOffset.
            var offsets = new Dictionary<int, int>();
            int constantSize = strike.IndexFormat is 2 or 5 ? glyphs.Max(g => 4 + g.Png.Length) : 0;
            foreach (var g in glyphs)
            {
                // Format 2 has a record for every glyph in the range: gaps get an empty one.
                for (int gap = offsets.Count == 0 ? first : offsets.Keys.Max() + 1; strike.IndexFormat == 2 && gap < g.GlyphId; gap++)
                {
                    offsets[gap] = cbdt.Count - imageDataOffset;
                    cbdt.AddRange(new byte[constantSize]);
                }

                offsets[g.GlyphId] = cbdt.Count - imageDataOffset;
                var data = GlyphData(strike.ImageFormat, g);
                cbdt.AddRange(data);
                if (constantSize > 0)
                    cbdt.AddRange(new byte[constantSize - data.Length]);
            }

            int end = cbdt.Count - imageDataOffset;

            // IndexSubtableList (one record) followed by its subtable.
            int listOffset = cblc.Count;
            var subtable = new List<byte>();
            subtable.AddRange(U16(strike.IndexFormat));
            subtable.AddRange(U16(strike.ImageFormat));
            subtable.AddRange(U32((uint)imageDataOffset));
            switch (strike.IndexFormat)
            {
                case 1:
                case 3:
                    for (int id = first; id <= last + 1; id++)
                    {
                        int offset = id > last ? end : offsets.TryGetValue(id, out int o) ? o : NextOffset(offsets, id, end);
                        subtable.AddRange(strike.IndexFormat == 1 ? U32((uint)offset) : U16(offset));
                    }

                    if (subtable.Count % 4 != 0)
                        subtable.AddRange(new byte[2]);
                    break;

                case 2:
                    subtable.AddRange(U32((uint)constantSize));
                    subtable.AddRange(BigMetrics(glyphs[0]));
                    break;

                case 4:
                    subtable.AddRange(U32((uint)glyphs.Length));
                    foreach (var g in glyphs)
                    {
                        subtable.AddRange(U16(g.GlyphId));
                        subtable.AddRange(U16(offsets[g.GlyphId]));
                    }

                    subtable.AddRange(U16(0));
                    subtable.AddRange(U16(end));
                    break;

                case 5:
                    subtable.AddRange(U32((uint)constantSize));
                    subtable.AddRange(BigMetrics(glyphs[0]));
                    subtable.AddRange(U32((uint)glyphs.Length));
                    foreach (var g in glyphs)
                        subtable.AddRange(U16(g.GlyphId));
                    if (glyphs.Length % 2 != 0)
                        subtable.AddRange(new byte[2]);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(strikes), strike.IndexFormat, "index format");
            }

            cblc.AddRange(U16(first));
            cblc.AddRange(U16(last));
            cblc.AddRange(U32(8)); // the subtable follows the one 8-byte record
            cblc.AddRange(subtable);

            // BitmapSize record.
            var size = new byte[48];
            BinaryPrimitives.WriteUInt32BigEndian(size, (uint)listOffset);
            BinaryPrimitives.WriteUInt32BigEndian(size.AsSpan(4), (uint)(8 + subtable.Count));
            BinaryPrimitives.WriteUInt32BigEndian(size.AsSpan(8), 1);
            size[16] = (byte)strike.Ppem; // hori.ascender
            BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(40), (ushort)first);
            BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(42), (ushort)last);
            size[44] = (byte)strike.Ppem;
            size[45] = (byte)strike.Ppem;
            size[46] = 32;
            size[47] = 1; // HORIZONTAL_METRICS
            for (int i = 0; i < 48; i++)
                cblc[sizesStart + (48 * s) + i] = size[i];
        }

        return (cblc.ToArray(), cbdt.ToArray());
    }

    /// <summary>Builds an <c>sbix</c> table for a font with <paramref name="numGlyphs"/> glyphs.</summary>
    public static byte[] Sbix(int numGlyphs, params SbixStrike[] strikes)
    {
        var table = new List<byte>();
        table.AddRange(U16(1));
        table.AddRange(U16(1));
        table.AddRange(U32((uint)strikes.Length));
        int offsetsAt = table.Count;
        table.AddRange(new byte[4 * strikes.Length]);

        for (int s = 0; s < strikes.Length; s++)
        {
            int strikeStart = table.Count;
            WriteU32(table, offsetsAt + (4 * s), (uint)strikeStart);

            var strike = strikes[s];
            var entries = strike.Entries.ToDictionary(e => e.GlyphId);
            var strikeData = new List<byte>();
            strikeData.AddRange(U16(strike.Ppem));
            strikeData.AddRange(U16(strike.Ppi));
            int glyphOffsetsAt = strikeData.Count;
            strikeData.AddRange(new byte[4 * (numGlyphs + 1)]);

            for (int g = 0; g <= numGlyphs; g++)
            {
                WriteU32(strikeData, glyphOffsetsAt + (4 * g), (uint)strikeData.Count);
                if (g < numGlyphs && entries.TryGetValue(g, out var entry))
                {
                    strikeData.AddRange(U16((ushort)entry.OriginX));
                    strikeData.AddRange(U16((ushort)entry.OriginY));
                    strikeData.AddRange(Encoding.ASCII.GetBytes(entry.GraphicType));
                    strikeData.AddRange(entry.Data);
                }
            }

            table.AddRange(strikeData);
        }

        return table.ToArray();
    }

    /// <summary>The data of an sbix 'dupe' record.</summary>
    public static byte[] Dupe(int glyphId) => U16(glyphId);

    /// <summary>Roboto with its outlines removed and the given tables added: a bitmap-only face.</summary>
    public static byte[] BitmapOnlyRoboto(params (string Tag, byte[] Data)[] tables)
    {
        var replacements = new Dictionary<string, byte[]?> { ["glyf"] = null, ["loca"] = null };
        foreach (var (tag, data) in tables)
            replacements[tag] = data;
        return SyntheticFonts.WithTables(TestFonts.RobotoRegularBytes(), replacements);
    }

    /// <summary>Roboto (outlines kept) with the given tables added: a mixed face.</summary>
    public static byte[] MixedRoboto(params (string Tag, byte[] Data)[] tables) => SyntheticFonts.RobotoWith(tables);

    /// <summary>Roboto's glyph index for a character.</summary>
    public static int RobotoGlyph(char c) => RobotoFace.Value.GetGlyphIndex(c);

    /// <summary>Roboto's <c>maxp.numGlyphs</c>.</summary>
    public static int RobotoGlyphCount => RobotoFace.Value.Maxp.NumGlyphs;

    /// <summary>Roboto's parsed face, for expected values.</summary>
    public static readonly Lazy<NativeFontFace> RobotoFace = new(() => NativeFontFace.Load(TestFonts.RobotoRegularBytes()));

    private static byte[] GlyphData(int imageFormat, CbdtGlyph g)
    {
        var data = new List<byte>();
        switch (imageFormat)
        {
            case 17:
                data.AddRange(SmallMetrics(g));
                break;
            case 18:
                data.AddRange(BigMetrics(g));
                break;
            case 19:
                break;
            default:
                // Any other EBDT format: the raw bytes stand in for its (unsupported) data.
                data.AddRange(SmallMetrics(g));
                data.AddRange(g.Png);
                return data.ToArray();
        }

        data.AddRange(U32((uint)g.Png.Length));
        data.AddRange(g.Png);
        return data.ToArray();
    }

    private static byte[] SmallMetrics(CbdtGlyph g) =>
        [(byte)g.Height, (byte)g.Width, unchecked((byte)(sbyte)g.BearingX), unchecked((byte)(sbyte)g.BearingY), (byte)g.Advance];

    private static byte[] BigMetrics(CbdtGlyph g) =>
        [(byte)g.Height, (byte)g.Width, unchecked((byte)(sbyte)g.BearingX), unchecked((byte)(sbyte)g.BearingY), (byte)g.Advance, 0, 0, (byte)g.Height];

    private static int NextOffset(Dictionary<int, int> offsets, int id, int end) =>
        offsets.Where(kv => kv.Key > id).Select(kv => kv.Value).DefaultIfEmpty(end).Min();

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] U32(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void WriteU32(List<byte> list, int at, uint value)
    {
        var bytes = U32(value);
        for (int i = 0; i < 4; i++)
            list[at + i] = bytes[i];
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typeAndData));
        output.Write(crc);
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}
