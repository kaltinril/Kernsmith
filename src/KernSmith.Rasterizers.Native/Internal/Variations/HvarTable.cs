namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The <c>HVAR</c> (horizontal metrics variations) table. Only advance-width variation is read:
/// side-bearing variation matters to layout engines that skip outlines, while this rasterizer
/// always has the varied outline in hand.
/// </summary>
internal sealed class HvarTable
{
    private readonly ItemVariationStore _store;
    private readonly DeltaSetIndexMap? _advanceMap;

    private HvarTable(ItemVariationStore store, DeltaSetIndexMap? advanceMap)
    {
        _store = store;
        _advanceMap = advanceMap;
    }

    /// <summary>The store the advance deltas live in; region scalars are computed against it.</summary>
    public ItemVariationStore Store => _store;

    /// <summary>Parses the header, the item variation store and the advance-width map.</summary>
    /// <exception cref="FontFormatException">If the table is truncated.</exception>
    public static HvarTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        reader.Skip(4);                         // majorVersion, minorVersion
        uint storeOffset = reader.ReadUInt32();
        uint advanceMapOffset = reader.ReadUInt32();

        var store = ItemVariationStore.Parse(data[checked((int)storeOffset)..]);
        var advanceMap = advanceMapOffset == 0 ? null : DeltaSetIndexMap.Parse(data[checked((int)advanceMapOffset)..]);
        return new HvarTable(store, advanceMap);
    }

    /// <summary>
    /// The advance-width delta for a glyph, in font units. Without a mapping subtable the glyph ID
    /// is the inner index of outer subtable 0.
    /// </summary>
    public float GetAdvanceDelta(int glyphIndex, ReadOnlySpan<float> regionScalars)
    {
        var (outer, inner) = _advanceMap?.Map(glyphIndex) ?? (0, (ushort)glyphIndex);
        return _store.GetDelta(outer, inner, regionScalars);
    }
}
