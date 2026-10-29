namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The <c>MVAR</c> (metrics variations) table: font-wide metric deltas keyed by value tag —
/// 'hcla' and 'hcld' (OS/2 usWinAscent/usWinDescent), 'hasc', 'xhgt' and so on.
/// </summary>
internal sealed class MvarTable
{
    private readonly Dictionary<string, (ushort Outer, ushort Inner)> _records;

    private MvarTable(Dictionary<string, (ushort Outer, ushort Inner)> records, ItemVariationStore? store)
    {
        _records = records;
        Store = store;
    }

    /// <summary>The store the deltas live in; null when the table has no value records.</summary>
    public ItemVariationStore? Store { get; }

    /// <summary>Parses the value records and the item variation store.</summary>
    /// <exception cref="FontFormatException">If the table is truncated.</exception>
    public static MvarTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        reader.Skip(6);                         // majorVersion, minorVersion, reserved
        int recordSize = reader.ReadUInt16();
        int recordCount = reader.ReadUInt16();
        int storeOffset = reader.ReadUInt16();
        int recordsStart = reader.Position;

        // valueRecordSize may grow in later minor versions; only the first 8 bytes are known.
        if (recordCount > 0 && recordSize < 8)
            throw new FontFormatException("MVAR", 6, $"valueRecordSize {recordSize} is smaller than a value record.");

        var records = new Dictionary<string, (ushort Outer, ushort Inner)>(recordCount, StringComparer.Ordinal);
        for (int i = 0; i < recordCount; i++)
        {
            reader.Seek(recordsStart + (i * recordSize));
            string tag = reader.ReadTag();
            records[tag] = (reader.ReadUInt16(), reader.ReadUInt16());
        }

        var store = recordCount > 0 && storeOffset != 0 ? ItemVariationStore.Parse(data[storeOffset..]) : null;
        return new MvarTable(records, store);
    }

    /// <summary>The delta for a value tag, in font units; 0 for a tag the table does not vary.</summary>
    public float GetDelta(string tag, ReadOnlySpan<float> regionScalars)
    {
        if (Store is null || !_records.TryGetValue(tag, out var index))
            return 0f;

        return Store.GetDelta(index.Outer, index.Inner, regionScalars);
    }
}
