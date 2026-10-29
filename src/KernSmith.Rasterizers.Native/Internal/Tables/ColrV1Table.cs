using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>A <c>COLR</c> clip box in font units, with the variation index of its corners (format 2).</summary>
/// <param name="XMin">Left edge.</param>
/// <param name="YMin">Bottom edge.</param>
/// <param name="XMax">Right edge.</param>
/// <param name="YMax">Top edge.</param>
/// <param name="VarIndexBase">The first of four variation indices (xMin, yMin, xMax, yMax), or
/// <see cref="ColrV1Table.NoVariation"/> for a static (format 1) box.</param>
internal readonly record struct ColrClipBox(short XMin, short YMin, short XMax, short YMax, uint VarIndexBase);

/// <summary>
/// The version 1 part of a parsed <c>COLR</c> table: the base glyph paint records, the layer
/// list, the clip list and the table's own variation data. The paint tables themselves are read
/// on demand from <see cref="Data"/> by <c>ColrPaintReader</c>, because a paint graph's values
/// depend on the selected palette and variable-font instance.
/// </summary>
/// <remarks>
/// The lists are bounds-checked at parse time; the offsets they hold are not — a paint reader
/// validates every offset it follows, since a single bad paint must only drop that paint.
/// </remarks>
internal sealed class ColrV1Table
{
    /// <summary>A <c>varIndexBase</c> meaning "no variation data".</summary>
    public const uint NoVariation = 0xFFFFFFFF;

    private const int HeaderSize = 34;

    private readonly ushort[] _baseGlyphIds;
    private readonly long[] _basePaintOffsets;
    private readonly long[] _layerPaintOffsets;
    private readonly ushort[] _clipStarts;
    private readonly ushort[] _clipEnds;
    private readonly long[] _clipBoxOffsets;

    private ColrV1Table(
        ReadOnlyMemory<byte> data,
        ushort[] baseGlyphIds,
        long[] basePaintOffsets,
        long[] layerPaintOffsets,
        ushort[] clipStarts,
        ushort[] clipEnds,
        long[] clipBoxOffsets,
        DeltaSetIndexMap? varIndexMap,
        ItemVariationStore? variationStore)
    {
        Data = data;
        _baseGlyphIds = baseGlyphIds;
        _basePaintOffsets = basePaintOffsets;
        _layerPaintOffsets = layerPaintOffsets;
        _clipStarts = clipStarts;
        _clipEnds = clipEnds;
        _clipBoxOffsets = clipBoxOffsets;
        VarIndexMap = varIndexMap;
        VariationStore = variationStore;
    }

    /// <summary>The whole <c>COLR</c> table; paint offsets are absolute positions in it.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The number of base glyph paint records.</summary>
    public int BaseGlyphCount => _baseGlyphIds.Length;

    /// <summary>The number of paints in the layer list.</summary>
    public int LayerCount => _layerPaintOffsets.Length;

    /// <summary>The number of clip records.</summary>
    public int ClipCount => _clipStarts.Length;

    /// <summary>Maps variation indices to delta sets; null for the implicit identity mapping.</summary>
    public DeltaSetIndexMap? VarIndexMap { get; }

    /// <summary>The table's variation deltas, or null when its paints do not vary.</summary>
    public ItemVariationStore? VariationStore { get; }

    /// <summary>The glyph of base glyph paint record <paramref name="index"/>.</summary>
    public int GetBaseGlyph(int index) => _baseGlyphIds[index];

    /// <summary>
    /// Finds a glyph's root paint (binary search: the spec requires the records sorted by glyph ID).
    /// </summary>
    /// <param name="glyphIndex">The base glyph.</param>
    /// <param name="paintOffset">The root paint's absolute offset in <see cref="Data"/>.</param>
    /// <returns>False when the glyph has no version 1 color definition.</returns>
    public bool TryGetBasePaint(int glyphIndex, out long paintOffset)
    {
        int lo = 0;
        int hi = _baseGlyphIds.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int id = _baseGlyphIds[mid];
            if (id == glyphIndex)
            {
                paintOffset = _basePaintOffsets[mid];
                return true;
            }

            if (id < glyphIndex)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        paintOffset = 0;
        return false;
    }

    /// <summary>The absolute offset of layer list paint <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If the index is at or past <see cref="LayerCount"/>.</exception>
    public long GetLayerPaint(long index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((ulong)index, (ulong)_layerPaintOffsets.Length, nameof(index));
        return _layerPaintOffsets[index];
    }

    /// <summary>Finds the clip box for a glyph, by the clip records' glyph ranges.</summary>
    /// <returns>False when the glyph has no clip box, or its box is malformed.</returns>
    public bool TryGetClipBox(int glyphIndex, out ColrClipBox box)
    {
        box = default;
        int lo = 0;
        int hi = _clipStarts.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (glyphIndex < _clipStarts[mid])
            {
                hi = mid - 1;
            }
            else if (glyphIndex > _clipEnds[mid])
            {
                lo = mid + 1;
            }
            else
            {
                return TryReadClipBox(_clipBoxOffsets[mid], out box);
            }
        }

        return false;
    }

    /// <summary>
    /// Parses the version 1 header and lists of a <c>COLR</c> table.
    /// </summary>
    /// <param name="data">The raw <c>COLR</c> bytes.</param>
    /// <returns>Null for a version 0 table.</returns>
    /// <exception cref="FontFormatException">If a list or the variation data runs past the table.</exception>
    public static ColrV1Table? Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var reader = new FontReader(span);
        if (reader.Remaining < 2 || reader.ReadUInt16() != 1)
            return null;

        if (span.Length < HeaderSize)
            throw new FontFormatException("COLR", 0, $"the version 1 table is {span.Length} bytes, shorter than its {HeaderSize}-byte header.");

        reader.Seek(14);
        uint baseGlyphListOffset = reader.ReadUInt32();
        uint layerListOffset = reader.ReadUInt32();
        uint clipListOffset = reader.ReadUInt32();
        uint varIndexMapOffset = reader.ReadUInt32();
        uint variationStoreOffset = reader.ReadUInt32();

        ushort[] baseGlyphIds = [];
        long[] basePaintOffsets = [];
        if (baseGlyphListOffset != 0)
        {
            uint count = ReadCount(span, baseGlyphListOffset, 6, "BaseGlyphList");
            baseGlyphIds = new ushort[count];
            basePaintOffsets = new long[count];
            reader.Seek((int)baseGlyphListOffset + 4);
            for (int i = 0; i < count; i++)
            {
                baseGlyphIds[i] = reader.ReadUInt16();
                basePaintOffsets[i] = baseGlyphListOffset + (long)reader.ReadUInt32();
            }
        }

        long[] layerPaintOffsets = [];
        if (layerListOffset != 0)
        {
            uint count = ReadCount(span, layerListOffset, 4, "LayerList");
            layerPaintOffsets = new long[count];
            reader.Seek((int)layerListOffset + 4);
            for (int i = 0; i < count; i++)
                layerPaintOffsets[i] = layerListOffset + (long)reader.ReadUInt32();
        }

        ushort[] clipStarts = [];
        ushort[] clipEnds = [];
        long[] clipBoxOffsets = [];
        if (clipListOffset != 0)
        {
            // ClipList: uint8 format, uint32 numClips, then 7-byte records.
            CheckRange(span.Length, clipListOffset, 5, "ClipList header");
            reader.Seek((int)clipListOffset + 1);
            uint count = reader.ReadUInt32();
            CheckRange(span.Length, clipListOffset + 5, (long)count * 7, "ClipList records");
            clipStarts = new ushort[count];
            clipEnds = new ushort[count];
            clipBoxOffsets = new long[count];
            for (int i = 0; i < count; i++)
            {
                clipStarts[i] = reader.ReadUInt16();
                clipEnds[i] = reader.ReadUInt16();
                clipBoxOffsets[i] = clipListOffset + (long)ReadUInt24(ref reader);
            }
        }

        DeltaSetIndexMap? varIndexMap = null;
        ItemVariationStore? variationStore = null;
        if (variationStoreOffset != 0)
        {
            CheckRange(span.Length, variationStoreOffset, 1, "ItemVariationStore");
            variationStore = ItemVariationStore.Parse(span[(int)variationStoreOffset..]);

            if (varIndexMapOffset != 0)
            {
                CheckRange(span.Length, varIndexMapOffset, 1, "DeltaSetIndexMap");
                varIndexMap = DeltaSetIndexMap.Parse(span[(int)varIndexMapOffset..]);
            }
        }

        return new ColrV1Table(data, baseGlyphIds, basePaintOffsets, layerPaintOffsets,
            clipStarts, clipEnds, clipBoxOffsets, varIndexMap, variationStore);
    }

    /// <summary>Reads a 24-bit unsigned integer (an Offset24).</summary>
    public static uint ReadUInt24(ref FontReader reader) =>
        ((uint)reader.ReadUInt8() << 16) | reader.ReadUInt16();

    private bool TryReadClipBox(long offset, out ColrClipBox box)
    {
        box = default;
        var span = Data.Span;
        if (offset < 0 || offset + 9 > span.Length)
            return false;

        var reader = new FontReader(span);
        reader.Seek((int)offset);
        byte format = reader.ReadUInt8();
        if (format is not (1 or 2))
            return false;

        short xMin = reader.ReadInt16();
        short yMin = reader.ReadInt16();
        short xMax = reader.ReadInt16();
        short yMax = reader.ReadInt16();
        uint varIndexBase = NoVariation;
        if (format == 2)
        {
            if (reader.Remaining < 4)
                return false;
            varIndexBase = reader.ReadUInt32();
        }

        box = new ColrClipBox(xMin, yMin, xMax, yMax, varIndexBase);
        return true;
    }

    /// <summary>Reads a list's uint32 count and checks that its records fit in the table.</summary>
    private static uint ReadCount(ReadOnlySpan<byte> table, uint listOffset, int recordSize, string what)
    {
        CheckRange(table.Length, listOffset, 4, what + " header");
        var reader = new FontReader(table);
        reader.Seek((int)listOffset);
        uint count = reader.ReadUInt32();
        CheckRange(table.Length, listOffset + 4L, (long)count * recordSize, what + " records");
        return count;
    }

    /// <summary>Checks that <paramref name="length"/> bytes at <paramref name="offset"/> lie inside the table.</summary>
    private static void CheckRange(int tableLength, long offset, long length, string what)
    {
        if (offset + length > tableLength)
            throw new FontFormatException("COLR", (int)Math.Min(offset, int.MaxValue),
                $"the {what} ({length} bytes at offset {offset}) run past the end of the {tableLength}-byte table.");
    }
}
