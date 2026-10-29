namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// A DeltaSetIndexMap (formats 0 and 1): maps an item index — a glyph ID in <c>HVAR</c> — to a
/// packed (outer, inner) delta-set index into an <see cref="ItemVariationStore"/>.
/// </summary>
internal sealed class DeltaSetIndexMap
{
    private const byte InnerIndexBitCountMask = 0x0F;
    private const byte MapEntrySizeMask = 0x30;

    private readonly (ushort Outer, ushort Inner)[] _entries;

    private DeltaSetIndexMap((ushort Outer, ushort Inner)[] entries) => _entries = entries;

    /// <summary>Parses a map from the bytes starting at its header.</summary>
    /// <exception cref="FontFormatException">If the map is truncated.</exception>
    public static DeltaSetIndexMap Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        byte format = reader.ReadUInt8();
        byte entryFormat = reader.ReadUInt8();
        long mapCount = format == 0 ? reader.ReadUInt16() : reader.ReadUInt32();

        int entrySize = ((entryFormat & MapEntrySizeMask) >> 4) + 1;
        int innerBits = (entryFormat & InnerIndexBitCountMask) + 1;
        if (mapCount * entrySize > reader.Remaining)
            throw new FontFormatException("DeltaSetIndexMap", reader.Position,
                $"{mapCount} entries of {entrySize} bytes overrun the table.");

        var entries = new (ushort Outer, ushort Inner)[mapCount];
        for (long i = 0; i < mapCount; i++)
        {
            uint entry = 0;
            for (int b = 0; b < entrySize; b++)
                entry = (entry << 8) | reader.ReadUInt8();

            entries[i] = ((ushort)(entry >> innerBits), (ushort)(entry & ((1u << innerBits) - 1)));
        }

        return new DeltaSetIndexMap(entries);
    }

    /// <summary>
    /// The delta-set index for an item. Indices past the end use the last entry, as the spec
    /// directs; an empty map yields the no-variation index.
    /// </summary>
    public (ushort Outer, ushort Inner) Map(int index)
    {
        if (_entries.Length == 0)
            return (0xFFFF, 0xFFFF);

        return _entries[Math.Clamp(index, 0, _entries.Length - 1)];
    }
}
