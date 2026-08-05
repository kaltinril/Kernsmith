using System.Buffers.Binary;

namespace KernSmith.Font.Tables;

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

    /// <summary>Returns the advance width, in font design units, for the given glyph index.</summary>
    public ushort GetAdvanceWidth(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)_leftSideBearings.Length)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));

        // Tail glyphs reuse the final stored advance width.
        int advanceIndex = Math.Min(glyphIndex, _advanceWidths.Length - 1);
        return _advanceWidths[advanceIndex];
    }

    /// <summary>Returns the left side bearing, in font design units, for the given glyph index.</summary>
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
    /// <remarks>
    /// Unlike a <c>maxp</c>-aware parser, the total glyph count isn't known here, so the
    /// number of trailing left-side-bearing-only entries is derived from the table's
    /// remaining byte length: <c>(data.Length - numberOfHMetrics * 4) / 2</c>.
    /// </remarks>
    public static HmtxTable Parse(ReadOnlySpan<byte> data, int numberOfHMetrics)
    {
        if (numberOfHMetrics <= 0)
            throw new FontFormatException("hmtx", 0, $"numberOfHMetrics must be positive, got {numberOfHMetrics}.");

        var fullMetricsBytes = numberOfHMetrics * 4;
        if (data.Length < fullMetricsBytes)
            throw new FontFormatException("hmtx", 0,
                $"Table is too small for {numberOfHMetrics} full metrics entries (needs {fullMetricsBytes} bytes, has {data.Length}).");

        var trailingLsbCount = (data.Length - fullMetricsBytes) / 2;

        var advanceWidths = new ushort[numberOfHMetrics];
        var leftSideBearings = new short[numberOfHMetrics + trailingLsbCount];

        var offset = 0;
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advanceWidths[i] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset));
            leftSideBearings[i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset + 2));
            offset += 4;
        }

        for (var i = 0; i < trailingLsbCount; i++)
        {
            leftSideBearings[numberOfHMetrics + i] = BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset));
            offset += 2;
        }

        return new HmtxTable(advanceWidths, leftSideBearings);
    }
}
