namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>A <c>COLR</c> version 0 layer: the glyph drawn and the <c>CPAL</c> entry that fills it.</summary>
/// <param name="GlyphIndex">The layer's outline glyph.</param>
/// <param name="PaletteIndex">The palette entry, or <see cref="ColrTable.ForegroundPaletteIndex"/>.</param>
internal readonly record struct ColrLayerRecord(int GlyphIndex, int PaletteIndex);

/// <summary>
/// The version 0 part of a parsed <c>COLR</c> (color) table: base glyph records, each naming a
/// run of layer records drawn bottom to top.
/// </summary>
/// <remarks>
/// Versions 0 and 1 are read. A version 1 table keeps the version 0 fields at the same offsets
/// (it may leave them empty); its paint graphs are not parsed here, so a glyph described only by
/// a version 1 paint has no layers. The record arrays are bounds-checked at parse time; a base
/// glyph's layer range is returned as stored, and callers stop at <see cref="LayerCount"/>.
/// </remarks>
internal sealed class ColrTable
{
    /// <summary>The palette index that means "the text foreground color", not a CPAL entry.</summary>
    public const int ForegroundPaletteIndex = 0xFFFF;

    private const int HeaderSize = 14;
    private const int BaseGlyphRecordSize = 6;
    private const int LayerRecordSize = 4;

    private readonly ushort[] _baseGlyphIds;
    private readonly ushort[] _firstLayerIndices;
    private readonly ushort[] _layerCounts;
    private readonly ColrLayerRecord[] _layers;

    private ColrTable(int version, ushort[] baseGlyphIds, ushort[] firstLayerIndices, ushort[] layerCounts, ColrLayerRecord[] layers)
    {
        Version = version;
        _baseGlyphIds = baseGlyphIds;
        _firstLayerIndices = firstLayerIndices;
        _layerCounts = layerCounts;
        _layers = layers;
    }

    /// <summary>The table version, 0 or 1.</summary>
    public int Version { get; }

    /// <summary>The number of version 0 base glyph records.</summary>
    public int BaseGlyphCount => _baseGlyphIds.Length;

    /// <summary>The number of version 0 layer records.</summary>
    public int LayerCount => _layers.Length;

    /// <summary>
    /// Finds a glyph's base glyph record (binary search: the spec requires the records sorted
    /// by glyph ID).
    /// </summary>
    /// <returns>False when the glyph has no version 0 color layers.</returns>
    public bool TryGetLayerRange(int glyphIndex, out int firstLayer, out int layerCount)
    {
        int lo = 0;
        int hi = _baseGlyphIds.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int id = _baseGlyphIds[mid];
            if (id == glyphIndex)
            {
                firstLayer = _firstLayerIndices[mid];
                layerCount = _layerCounts[mid];
                return true;
            }

            if (id < glyphIndex)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        firstLayer = 0;
        layerCount = 0;
        return false;
    }

    /// <summary>Returns layer record <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If the index is at or past <see cref="LayerCount"/>.</exception>
    public ColrLayerRecord GetLayer(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_layers.Length, nameof(index));
        return _layers[index];
    }

    /// <summary>Parses the <c>COLR</c> table's version 0 records.</summary>
    /// <param name="data">The raw <c>COLR</c> bytes.</param>
    /// <exception cref="FontFormatException">
    /// If the version is unknown or a record array runs past the end of the table.
    /// </exception>
    public static ColrTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        if (reader.Remaining < HeaderSize)
            throw new FontFormatException("COLR", 0, $"the table is {data.Length} bytes, shorter than its {HeaderSize}-byte header.");

        int version = reader.ReadUInt16();
        if (version > 1)
            throw new FontFormatException("COLR", 0, $"unsupported version {version}.");

        int baseGlyphCount = reader.ReadUInt16();
        uint baseGlyphOffset = reader.ReadUInt32();
        uint layerOffset = reader.ReadUInt32();
        int layerCount = reader.ReadUInt16();

        var baseGlyphIds = new ushort[baseGlyphCount];
        var firstLayerIndices = new ushort[baseGlyphCount];
        var layerCounts = new ushort[baseGlyphCount];
        if (baseGlyphCount > 0)
        {
            reader.Seek(CheckedRange(data.Length, baseGlyphOffset, (long)baseGlyphCount * BaseGlyphRecordSize, "base glyph records"));
            for (int i = 0; i < baseGlyphCount; i++)
            {
                baseGlyphIds[i] = reader.ReadUInt16();
                firstLayerIndices[i] = reader.ReadUInt16();
                layerCounts[i] = reader.ReadUInt16();
            }
        }

        var layers = new ColrLayerRecord[layerCount];
        if (layerCount > 0)
        {
            reader.Seek(CheckedRange(data.Length, layerOffset, (long)layerCount * LayerRecordSize, "layer records"));
            for (int i = 0; i < layerCount; i++)
                layers[i] = new ColrLayerRecord(reader.ReadUInt16(), reader.ReadUInt16());
        }

        return new ColrTable(version, baseGlyphIds, firstLayerIndices, layerCounts, layers);
    }

    /// <summary>Checks that <paramref name="length"/> bytes at <paramref name="offset"/> lie inside the table.</summary>
    private static int CheckedRange(int tableLength, uint offset, long length, string what)
    {
        if (offset + length > tableLength)
            throw new FontFormatException("COLR", (int)Math.Min(offset, int.MaxValue),
                $"the {what} ({length} bytes at offset {offset}) run past the end of the {tableLength}-byte table.");
        return (int)offset;
    }
}
