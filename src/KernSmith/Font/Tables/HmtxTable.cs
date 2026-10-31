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
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _numberOfHMetrics;

    private HmtxTable(ReadOnlyMemory<byte> data, int numberOfHMetrics, int glyphCount)
    {
        _data = data;
        _numberOfHMetrics = numberOfHMetrics;
        GlyphCount = glyphCount;
    }

    public int GlyphCount { get; }

    public ushort GetAdvanceWidth(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));

        // Tail glyphs reuse the final stored advance width.
        int advanceIndex = Math.Min(glyphIndex, _numberOfHMetrics - 1);
        return BinaryPrimitives.ReadUInt16BigEndian(_data.Span.Slice(advanceIndex * 4));
    }

    public short GetLeftSideBearing(int glyphIndex)
    {
        if ((uint)glyphIndex >= (uint)GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex));

        var offset = glyphIndex < _numberOfHMetrics
            ? glyphIndex * 4 + 2
            : _numberOfHMetrics * 4 + (glyphIndex - _numberOfHMetrics) * 2;
        return BinaryPrimitives.ReadInt16BigEndian(_data.Span.Slice(offset));
    }

    /// <summary>
    /// Validates the table's size and keeps the bytes; entries are read on demand. The font
    /// data must stay unchanged while the table is in use (the parser owns its copy).
    /// </summary>
    public static HmtxTable Parse(ReadOnlyMemory<byte> data, int numberOfHMetrics)
    {
        if (numberOfHMetrics <= 0)
            throw new FontFormatException("hmtx", 0, $"numberOfHMetrics must be positive, got {numberOfHMetrics}.");

        var fullMetricsBytes = numberOfHMetrics * 4;
        if (data.Length < fullMetricsBytes)
            throw new FontFormatException("hmtx", 0,
                $"Table is too small for {numberOfHMetrics} full metrics entries (needs {fullMetricsBytes} bytes, has {data.Length}).");

        var trailingLsbCount = (data.Length - fullMetricsBytes) / 2;
        return new HmtxTable(data, numberOfHMetrics, numberOfHMetrics + trailingLsbCount);
    }
}
