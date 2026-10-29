namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// An item variation store (OpenType Font Variations Common Table Formats): a region list and
/// subtables of delta-set rows, addressed by an (outer, inner) delta-set index. Shared by
/// <c>HVAR</c>, <c>MVAR</c> and the CFF2 VariationStore.
/// </summary>
/// <remarks>
/// Region scalars depend only on the instance, so callers compute them once per instance with
/// <see cref="ComputeRegionScalars"/> and pass them to every <see cref="GetDelta"/>.
/// </remarks>
internal sealed class ItemVariationStore
{
    private const ushort LongWords = 0x8000;
    private const ushort WordDeltaCountMask = 0x7FFF;

    /// <summary>The (outer, inner) pair meaning "no variation data".</summary>
    private const ushort NoVariationIndex = 0xFFFF;

    private readonly int _axisCount;
    private readonly float[] _regions;          // regionCount x axisCount x (start, peak, end)
    private readonly int _regionCount;
    private readonly ItemVariationData?[] _subtables;

    private ItemVariationStore(int axisCount, float[] regions, int regionCount, ItemVariationData?[] subtables)
    {
        _axisCount = axisCount;
        _regions = regions;
        _regionCount = regionCount;
        _subtables = subtables;
    }

    /// <summary>Parses a store from the bytes starting at its header.</summary>
    /// <exception cref="FontFormatException">If the store is truncated or inconsistent.</exception>
    public static ItemVariationStore Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        reader.Skip(2);                         // format
        uint regionListOffset = reader.ReadUInt32();
        int subtableCount = reader.ReadUInt16();
        var subtableOffsets = new uint[subtableCount];
        for (int i = 0; i < subtableCount; i++)
            subtableOffsets[i] = reader.ReadUInt32();

        reader.Seek(regionListOffset);
        int axisCount = reader.ReadUInt16();
        int regionCount = reader.ReadUInt16() & 0x7FFF;

        // Checked against the bytes present before allocating: hostile counts can ask for
        // gigabytes, or overflow the array length.
        long coordinates = (long)regionCount * axisCount * 3;
        if (coordinates * 2 > reader.Remaining)
            throw new FontFormatException(
                $"ItemVariationStore region list declares {regionCount} regions over {axisCount} axes, more than its {reader.Remaining} bytes hold.");

        var regions = new float[coordinates];
        for (int i = 0; i < regions.Length; i++)
            regions[i] = reader.ReadF2Dot14();

        var subtables = new ItemVariationData?[subtableCount];
        for (int i = 0; i < subtableCount; i++)
        {
            if (subtableOffsets[i] != 0)
                subtables[i] = ItemVariationData.Parse(data, subtableOffsets[i], regionCount);
        }

        return new ItemVariationStore(axisCount, regions, regionCount, subtables);
    }

    /// <summary>The scalar of every region at one instance, indexed by region.</summary>
    public float[] ComputeRegionScalars(ReadOnlySpan<float> coordinates)
    {
        var scalars = new float[_regionCount];
        for (int region = 0; region < _regionCount; region++)
        {
            float scalar = 1f;
            int baseIndex = region * _axisCount * 3;
            for (int axis = 0; axis < _axisCount && !scalar.Equals(0f); axis++)
            {
                int at = baseIndex + (axis * 3);
                float coordinate = axis < coordinates.Length ? coordinates[axis] : 0f;
                scalar *= RegionScalar.ForAxis(_regions[at], _regions[at + 1], _regions[at + 2], coordinate);
            }

            scalars[region] = scalar;
        }

        return scalars;
    }

    /// <summary>The number of ItemVariationData subtables, NULL offsets included.</summary>
    public int SubtableCount => _subtables.Length;

    /// <summary>
    /// The scalars of the regions one ItemVariationData subtable selects, in its
    /// <c>regionIndexes</c> order — the order CFF2 <c>blend</c> lists its deltas in.
    /// </summary>
    /// <param name="outer">The subtable (a CFF2 <c>vsindex</c>).</param>
    /// <param name="regionScalars">Every region's scalar, from <see cref="ComputeRegionScalars"/>.</param>
    /// <exception cref="FontFormatException">If there is no such subtable.</exception>
    public float[] SubtableRegionScalars(int outer, ReadOnlySpan<float> regionScalars)
    {
        if ((uint)outer >= (uint)_subtables.Length || _subtables[outer] is not { } subtable)
            throw new FontFormatException($"ItemVariationStore has no ItemVariationData {outer}.");

        return subtable.RegionScalars(regionScalars);
    }

    /// <summary>
    /// The net adjustment for one delta set at the instance <paramref name="regionScalars"/>
    /// describes. Missing data — the no-variation index, a NULL or absent subtable, a row past
    /// the subtable's end — is zero.
    /// </summary>
    public float GetDelta(ushort outer, ushort inner, ReadOnlySpan<float> regionScalars)
    {
        if (outer == NoVariationIndex && inner == NoVariationIndex)
            return 0f;
        if (outer >= _subtables.Length || _subtables[outer] is not { } subtable)
            return 0f;

        return subtable.GetDelta(inner, regionScalars);
    }

    /// <summary>One ItemVariationData subtable, decoded to a dense delta matrix.</summary>
    private sealed class ItemVariationData
    {
        private readonly int _itemCount;
        private readonly ushort[] _regionIndexes;
        private readonly int[] _deltas;          // itemCount x regionIndexes.Length

        private ItemVariationData(int itemCount, ushort[] regionIndexes, int[] deltas)
        {
            _itemCount = itemCount;
            _regionIndexes = regionIndexes;
            _deltas = deltas;
        }

        public static ItemVariationData Parse(ReadOnlySpan<byte> store, long offset, int regionCount)
        {
            var reader = new FontReader(store);
            reader.Seek(offset);
            int itemCount = reader.ReadUInt16();
            ushort wordDeltaCount = reader.ReadUInt16();
            int columns = reader.ReadUInt16();

            bool longWords = (wordDeltaCount & LongWords) != 0;
            int wordColumns = wordDeltaCount & WordDeltaCountMask;
            if (wordColumns > columns)
                throw new FontFormatException("ItemVariationStore", (int)offset,
                    $"{wordColumns} word columns exceed the {columns} regions referenced.");

            var regionIndexes = new ushort[columns];
            for (int i = 0; i < columns; i++)
            {
                regionIndexes[i] = reader.ReadUInt16();
                if (regionIndexes[i] >= regionCount)
                    throw new FontFormatException("ItemVariationStore", reader.Position,
                        $"region index {regionIndexes[i]} is outside the {regionCount}-region list.");
            }

            // Each row: the "word" columns first, then the short ones; with LONG_WORDS the pair
            // is int32/int16, otherwise int16/int8.
            int rowBytes = longWords ? (wordColumns * 4) + ((columns - wordColumns) * 2) : (wordColumns * 2) + (columns - wordColumns);
            if ((long)itemCount * rowBytes > reader.Remaining)
                throw new FontFormatException("ItemVariationStore", (int)offset,
                    $"{itemCount} delta rows of {rowBytes} bytes run past the end of the store.");

            var deltas = new int[itemCount * columns];
            for (int item = 0; item < itemCount; item++)
            {
                int row = item * columns;
                for (int column = 0; column < columns; column++)
                {
                    bool wide = column < wordColumns;
                    deltas[row + column] = (longWords, wide) switch
                    {
                        (true, true) => reader.ReadInt32(),
                        (true, false) => reader.ReadInt16(),
                        (false, true) => reader.ReadInt16(),
                        (false, false) => reader.ReadInt8(),
                    };
                }
            }

            return new ItemVariationData(itemCount, regionIndexes, deltas);
        }

        public float[] RegionScalars(ReadOnlySpan<float> regionScalars)
        {
            var selected = new float[_regionIndexes.Length];
            for (int column = 0; column < selected.Length; column++)
            {
                int region = _regionIndexes[column];
                selected[column] = region < regionScalars.Length ? regionScalars[region] : 0f;
            }

            return selected;
        }

        public float GetDelta(int item, ReadOnlySpan<float> regionScalars)
        {
            if (item >= _itemCount)
                return 0f;

            int columns = _regionIndexes.Length;
            int row = item * columns;
            float total = 0f;
            for (int column = 0; column < columns; column++)
            {
                int region = _regionIndexes[column];
                if (region < regionScalars.Length)
                    total += _deltas[row + column] * regionScalars[region];
            }

            return total;
        }
    }
}
