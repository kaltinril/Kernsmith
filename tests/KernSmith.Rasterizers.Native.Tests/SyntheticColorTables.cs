using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Builds <c>COLR</c> version 0 and <c>CPAL</c> tables byte by byte, so layer order, palette
/// selection, the foreground entry and malformed data can be tested against glyphs whose shapes
/// are known (Roboto's), and against FreeType on the very same bytes.
/// </summary>
internal static class SyntheticColorTables
{
    /// <summary>A layer: the glyph drawn and the palette entry that fills it (0xFFFF = foreground).</summary>
    public readonly record struct Layer(int GlyphIndex, int PaletteIndex);

    /// <summary>The palette entry that means "the text foreground color".</summary>
    public const int Foreground = 0xFFFF;

    /// <summary>
    /// A version 0 <c>COLR</c> table. Base glyphs are written in ascending glyph order, as the
    /// spec requires; each one's layers are stored contiguously, bottom layer first.
    /// </summary>
    public static byte[] Colr(params (int BaseGlyph, Layer[] Layers)[] baseGlyphs)
    {
        var sorted = baseGlyphs.OrderBy(b => b.BaseGlyph).ToArray();
        int layerCount = sorted.Sum(b => b.Layers.Length);

        const int HeaderSize = 14;
        int baseOffset = HeaderSize;
        int layerOffset = baseOffset + (sorted.Length * 6);
        var table = new byte[layerOffset + (layerCount * 4)];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 0); // version
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)sorted.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)baseOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)layerOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], (ushort)layerCount);

        int firstLayer = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            var record = span[(baseOffset + (i * 6))..];
            BinaryPrimitives.WriteUInt16BigEndian(record, (ushort)sorted[i].BaseGlyph);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], (ushort)firstLayer);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], (ushort)sorted[i].Layers.Length);

            foreach (var layer in sorted[i].Layers)
            {
                var layerRecord = span[(layerOffset + (firstLayer * 4))..];
                BinaryPrimitives.WriteUInt16BigEndian(layerRecord, (ushort)layer.GlyphIndex);
                BinaryPrimitives.WriteUInt16BigEndian(layerRecord[2..], (ushort)layer.PaletteIndex);
                firstLayer++;
            }
        }

        return table;
    }

    /// <summary>
    /// A <c>CPAL</c> table with one color-record run per palette. Colors are given as
    /// 0xRRGGBBAA. Supplying <paramref name="paletteTypes"/> writes a version 1 table carrying
    /// them; otherwise the table is version 0.
    /// </summary>
    public static byte[] Cpal(uint[][] palettes, uint[]? paletteTypes = null)
    {
        int entries = palettes[0].Length;
        if (palettes.Any(p => p.Length != entries))
            throw new ArgumentException("Every palette must have the same number of entries.", nameof(palettes));

        bool v1 = paletteTypes is not null;
        int headerSize = 12 + (palettes.Length * 2) + (v1 ? 12 : 0);
        int recordsOffset = headerSize;
        int typesOffset = recordsOffset + (palettes.Length * entries * 4);
        var table = new byte[typesOffset + (v1 ? palettes.Length * 4 : 0)];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)(v1 ? 1 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)entries);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)palettes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)(palettes.Length * entries));
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)recordsOffset);
        for (int p = 0; p < palettes.Length; p++)
            BinaryPrimitives.WriteUInt16BigEndian(span[(12 + (p * 2))..], (ushort)(p * entries));

        if (v1)
        {
            int after = 12 + (palettes.Length * 2);
            BinaryPrimitives.WriteUInt32BigEndian(span[after..], (uint)typesOffset);
            // Palette labels and entry labels: absent.
            for (int p = 0; p < palettes.Length; p++)
                BinaryPrimitives.WriteUInt32BigEndian(span[(typesOffset + (p * 4))..], paletteTypes![p]);
        }

        for (int p = 0; p < palettes.Length; p++)
        {
            for (int e = 0; e < entries; e++)
            {
                uint rgba = palettes[p][e];
                int at = recordsOffset + (((p * entries) + e) * 4);
                table[at] = (byte)(rgba >> 8);       // blue
                table[at + 1] = (byte)(rgba >> 16);  // green
                table[at + 2] = (byte)(rgba >> 24);  // red
                table[at + 3] = (byte)rgba;          // alpha
            }
        }

        return table;
    }
}
