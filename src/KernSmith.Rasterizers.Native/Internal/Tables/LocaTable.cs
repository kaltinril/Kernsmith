namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>loca</c> (index to location) table: where each glyph's outline lives
/// inside the <c>glyf</c> table.
/// </summary>
/// <remarks>
/// The table holds <c>numGlyphs + 1</c> offsets, so glyph <c>i</c> occupies the byte range
/// <c>[offset[i], offset[i + 1])</c>. The short format stores each offset divided by two;
/// the long format stores it verbatim. <c>head.indexToLocFormat</c> selects the format.
/// When two consecutive offsets are equal, the glyph has no outline (a space, for example)
/// — that is normal, not an error.
/// </remarks>
internal sealed class LocaTable
{
    private readonly uint[] _offsets;

    private LocaTable(uint[] offsets) => _offsets = offsets;

    /// <summary>Number of glyphs covered by this table.</summary>
    public int GlyphCount => _offsets.Length - 1;

    /// <summary>
    /// Returns the byte range of a glyph's entry within the <c>glyf</c> table.
    /// A length of zero means the glyph has no outline.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the glyph index is outside the table.</exception>
    public (int Offset, int Length) GetGlyphRange(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));

        uint start = _offsets[glyphIndex];
        uint end = _offsets[glyphIndex + 1];
        return ((int)start, (int)(end - start));
    }

    /// <summary>
    /// Parses the <c>loca</c> table.
    /// </summary>
    /// <param name="data">The raw <c>loca</c> bytes.</param>
    /// <param name="numGlyphs">From <c>maxp</c>: total glyph count in the font.</param>
    /// <param name="longFormat">From <c>head.indexToLocFormat</c>: true for 32-bit offsets.</param>
    /// <exception cref="FontFormatException">If the table is truncated or its offsets decrease.</exception>
    public static LocaTable Parse(ReadOnlySpan<byte> data, int numGlyphs, bool longFormat)
    {
        if (numGlyphs <= 0)
            throw new FontFormatException("loca", 0, $"numGlyphs must be positive, got {numGlyphs}.");

        int entryCount = numGlyphs + 1;
        int required = entryCount * (longFormat ? 4 : 2);
        if (data.Length < required)
            throw new FontFormatException("loca", 0,
                $"table is {data.Length} bytes but {entryCount} {(longFormat ? "long" : "short")} offsets need {required}.");

        var reader = new FontReader(data);
        var offsets = new uint[entryCount];

        for (int i = 0; i < entryCount; i++)
        {
            // The short format halves every offset, which is why it can only address
            // glyf tables up to 128 KB and requires 2-byte-aligned glyph entries.
            offsets[i] = longFormat ? reader.ReadUInt32() : (uint)reader.ReadUInt16() * 2;

            if (i > 0 && offsets[i] < offsets[i - 1])
                throw new FontFormatException("loca", reader.Position,
                    $"offset {i} ({offsets[i]}) is less than offset {i - 1} ({offsets[i - 1]}).");
        }

        return new LocaTable(offsets);
    }
}
