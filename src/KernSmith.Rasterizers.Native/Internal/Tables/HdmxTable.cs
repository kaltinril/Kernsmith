namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>hdmx</c> (horizontal device metrics) table: for each pixel size the designer
/// recorded, one byte of hinted advance width per glyph — the advances the font's own
/// bytecode produces at that integer ppem, which FreeType's TrueType driver and GDI report
/// instead of running the interpreter for them.
/// </summary>
/// <remarks>
/// Layout: <c>version</c> (uint16), <c>numRecords</c> (int16), <c>sizeDeviceRecord</c> (int32),
/// then <c>numRecords</c> device records of <c>pixelSize</c> (uint8), <c>maxWidth</c> (uint8)
/// and <c>widths[numGlyphs]</c> (uint8), each padded to <c>sizeDeviceRecord</c> bytes (a
/// multiple of four). The table is optional and advisory, so a malformed one is ignored
/// (<see cref="TryParse"/> returns null) rather than failing the font.
/// </remarks>
internal sealed class HdmxTable
{
    private const int HeaderSize = 8;
    private const int RecordHeaderSize = 2;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _sizeDeviceRecord;
    private readonly int _numGlyphs;

    /// <summary>Record index by pixel size; the first record for a size wins.</summary>
    private readonly Dictionary<int, int> _recordBySize;

    private HdmxTable(ReadOnlyMemory<byte> data, int sizeDeviceRecord, int numGlyphs, Dictionary<int, int> recordBySize)
    {
        _data = data;
        _sizeDeviceRecord = sizeDeviceRecord;
        _numGlyphs = numGlyphs;
        _recordBySize = recordBySize;
    }

    /// <summary>The pixel sizes the table has a record for, ascending.</summary>
    public IReadOnlyList<int> PixelSizes { get; private init; } = [];

    /// <summary>True when the table has a record for the given integer ppem.</summary>
    public bool HasRecord(int ppem) => _recordBySize.ContainsKey(ppem);

    /// <summary>
    /// The recorded advance, in whole pixels, of a glyph at an integer ppem.
    /// </summary>
    /// <returns>False when the table has no record for that ppem or the glyph index is outside the font.</returns>
    public bool TryGetAdvance(int ppem, int glyphIndex, out int advance)
    {
        advance = 0;
        if ((uint)glyphIndex >= (uint)_numGlyphs || !_recordBySize.TryGetValue(ppem, out int record))
            return false;

        advance = _data.Span[HeaderSize + (record * _sizeDeviceRecord) + RecordHeaderSize + glyphIndex];
        return true;
    }

    /// <summary>
    /// Parses the table; null when it is malformed in any way — too short for its header, a
    /// record size that cannot hold a width per glyph, records that run past the table — or
    /// records nothing.
    /// </summary>
    /// <param name="data">The raw <c>hdmx</c> bytes.</param>
    /// <param name="numGlyphs">From <c>maxp</c>: the number of widths in every record.</param>
    public static HdmxTable? TryParse(ReadOnlyMemory<byte> data, int numGlyphs)
    {
        var span = data.Span;
        if (numGlyphs <= 0 || span.Length < HeaderSize)
            return null;

        var reader = new FontReader(span);
        reader.ReadUInt16(); // version: 0 today; the layout has never changed, so any value is read the same way.
        int numRecords = reader.ReadInt16();
        int sizeDeviceRecord = reader.ReadInt32();
        if (numRecords <= 0 || sizeDeviceRecord < RecordHeaderSize + numGlyphs)
            return null;

        // Records that would run past the table: the whole table is suspect, not just the tail.
        long end = HeaderSize + ((long)numRecords * sizeDeviceRecord);
        if (end > span.Length)
            return null;

        var recordBySize = new Dictionary<int, int>(numRecords);
        var sizes = new List<int>(numRecords);
        for (int i = 0; i < numRecords; i++)
        {
            int pixelSize = span[HeaderSize + (i * sizeDeviceRecord)];
            if (recordBySize.TryAdd(pixelSize, i))
                sizes.Add(pixelSize);
        }

        sizes.Sort();
        return new HdmxTable(data, sizeDeviceRecord, numGlyphs, recordBySize) { PixelSizes = sizes };
    }
}
