namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>hmtx</c> (horizontal metrics) table: per-glyph advance width and
/// left side bearing, in font design units.
/// </summary>
/// <remarks>
/// The table stores <c>numberOfHMetrics</c> full (advance, lsb) pairs followed by
/// left side bearings only. Glyphs at or beyond <c>numberOfHMetrics</c> reuse the
/// last advance width but still carry their own left side bearing — this is the
/// standard monospaced-tail compression used by the SFNT format.
/// </remarks>
internal sealed class HmtxTable
{
    private readonly ushort[] _advanceWidths;
    private readonly short[] _leftSideBearings;

    private HmtxTable(ushort[] advanceWidths, short[] leftSideBearings)
    {
        _advanceWidths = advanceWidths;
        _leftSideBearings = leftSideBearings;
    }

    /// <summary>Number of glyphs covered by this table.</summary>
    public int GlyphCount => _leftSideBearings.Length;

    /// <summary>Returns the advance width, in font units, for the given glyph index.</summary>
    public ushort GetAdvanceWidth(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)_leftSideBearings.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));

        // Tail glyphs reuse the final stored advance width.
        int advanceIndex = Math.Min(glyphIndex, _advanceWidths.Length - 1);
        return _advanceWidths[advanceIndex];
    }

    /// <summary>Returns the left side bearing, in font units, for the given glyph index.</summary>
    public short GetLeftSideBearing(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)_leftSideBearings.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));
        return _leftSideBearings[glyphIndex];
    }

    /// <summary>
    /// Parses the <c>hmtx</c> table.
    /// </summary>
    /// <param name="data">The raw <c>hmtx</c> bytes.</param>
    /// <param name="numberOfHMetrics">From <c>hhea</c>: count of full (advance, lsb) pairs.</param>
    /// <param name="numGlyphs">From <c>maxp</c>: total glyph count in the font.</param>
    public static HmtxTable Parse(ReadOnlySpan<byte> data, int numberOfHMetrics, int numGlyphs)
    {
        if (numberOfHMetrics <= 0)
            throw new FontFormatException("hmtx", 0, $"numberOfHMetrics must be positive, got {numberOfHMetrics}.");
        if (numGlyphs < numberOfHMetrics)
            throw new FontFormatException("hmtx", 0,
                $"numGlyphs ({numGlyphs}) is less than numberOfHMetrics ({numberOfHMetrics}).");

        var reader = new FontReader(data);

        var advanceWidths = new ushort[numberOfHMetrics];
        var leftSideBearings = new short[numGlyphs];

        for (int i = 0; i < numberOfHMetrics; i++)
        {
            advanceWidths[i] = reader.ReadUInt16();
            leftSideBearings[i] = reader.ReadInt16();
        }

        for (int i = numberOfHMetrics; i < numGlyphs; i++)
            leftSideBearings[i] = reader.ReadInt16();

        return new HmtxTable(advanceWidths, leftSideBearings);
    }
}
