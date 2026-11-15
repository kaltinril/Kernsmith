using System.Buffers.Binary;
using KernSmith.Font.Tables;

namespace KernSmith.Font;

/// <summary>
/// The deltas of the <c>GDEF</c> ItemVariationStore at one variation instance: what a GPOS
/// ValueRecord's VariationIndex table adds to its value, so kerning follows the instance.
/// </summary>
/// <remarks>
/// Core cannot reference the Native rasterizer, which has its own store and normalization
/// (<c>AxisNormalizer</c>, <c>AvarTable</c>, <c>RegionScalar</c>). The normalization here follows
/// the same steps and must stay in step with it, or kerning and glyphs land on different
/// instances. Every read is bounds-checked and malformed data means "no delta": the font is
/// untrusted, and a damaged store must cost no more than its own bytes.
/// </remarks>
internal sealed class ItemVariationDeltas
{
    private const int One = 0x10000;            // 1.0 in 16.16
    private const int NoVariationIndex = 0xFFFF;

    private readonly ReadOnlyMemory<byte> _store;
    private readonly double[] _regionScalars;
    private readonly int _dataCount;

    private ItemVariationDeltas(ReadOnlyMemory<byte> store, double[] regionScalars, int dataCount)
    {
        _store = store;
        _regionScalars = regionScalars;
        _dataCount = dataCount;
    }

    /// <summary>
    /// The store of <paramref name="gdef"/> at the instance <paramref name="userValues"/> select,
    /// or null when there is nothing to add: the default instance, or no usable store.
    /// </summary>
    /// <param name="gdef">The <c>GDEF</c> table.</param>
    /// <param name="avar">The <c>avar</c> table; empty if the font has none.</param>
    /// <param name="axes">The font's axes, in <c>fvar</c> order.</param>
    /// <param name="userValues">Requested user-scale values by axis tag.</param>
    public static ItemVariationDeltas? Create(
        ReadOnlyMemory<byte> gdef, ReadOnlySpan<byte> avar,
        IReadOnlyList<VariationAxis> axes, IReadOnlyDictionary<string, float> userValues)
    {
        var coordinates = NormalizeCoordinates(axes, userValues, avar);
        if (Array.TrueForAll(coordinates, c => c == 0))
            return null;

        // GDEF 1.3 header: version (4), four Offset16 (8), markGlyphSetsDefOffset (2),
        // itemVarStoreOffset (Offset32) at 14.
        var table = gdef.Span;
        if (table.Length < 18
            || BinaryPrimitives.ReadUInt16BigEndian(table) != 1
            || BinaryPrimitives.ReadUInt16BigEndian(table.Slice(2)) < 3)
            return null;

        var storeOffset = BinaryPrimitives.ReadUInt32BigEndian(table.Slice(14));
        if (storeOffset == 0 || storeOffset > (uint)(table.Length - 8))
            return null;

        // ItemVariationStore: format (2), variationRegionListOffset (Offset32),
        // itemVariationDataCount (2), itemVariationDataOffsets (Offset32 each).
        var storeMemory = gdef.Slice((int)storeOffset);
        var store = storeMemory.Span;
        if (BinaryPrimitives.ReadUInt16BigEndian(store) != 1)
            return null;

        var regionListOffset = BinaryPrimitives.ReadUInt32BigEndian(store.Slice(2));
        var dataCount = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(store.Slice(6)), (store.Length - 8) / 4);
        if (regionListOffset > (uint)(store.Length - 4))
            return null;

        // VariationRegionList: axisCount (2), regionCount (2), then per region and axis a
        // (start, peak, end) F2DOT14 triple. Checked against the bytes present before allocating.
        var regionList = store.Slice((int)regionListOffset);
        int axisCount = BinaryPrimitives.ReadUInt16BigEndian(regionList);
        var regionCount = BinaryPrimitives.ReadUInt16BigEndian(regionList.Slice(2)) & 0x7FFF;
        if ((long)regionCount * axisCount * 6 > regionList.Length - 4)
            return null;

        var scalars = new double[regionCount];
        for (var region = 0; region < regionCount; region++)
        {
            var scalar = 1.0;
            var at = 4 + region * axisCount * 6;
            for (var axis = 0; axis < axisCount && !scalar.Equals(0.0); axis++, at += 6)
            {
                int start = BinaryPrimitives.ReadInt16BigEndian(regionList.Slice(at));
                int peak = BinaryPrimitives.ReadInt16BigEndian(regionList.Slice(at + 2));
                int end = BinaryPrimitives.ReadInt16BigEndian(regionList.Slice(at + 4));
                scalar *= AxisScalar(start, peak, end, axis < coordinates.Length ? coordinates[axis] : 0);
            }

            scalars[region] = scalar;
        }

        return new ItemVariationDeltas(storeMemory, scalars, dataCount);
    }

    /// <summary>
    /// The delta of one delta set at the instance, unrounded; zero for the no-variation index
    /// and for anything the store does not hold.
    /// </summary>
    /// <param name="outer">The ItemVariationData subtable.</param>
    /// <param name="inner">The delta-set row within it.</param>
    /// <param name="cost">The steps the read took, for the caller's work budget: a row is as
    /// wide as its subtable's region list, which a hostile font makes as wide as it likes.</param>
    public double GetDelta(int outer, int inner, out int cost)
    {
        cost = 1;
        if ((outer == NoVariationIndex && inner == NoVariationIndex) || outer >= _dataCount)
            return 0;

        var store = _store.Span;
        var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(store.Slice(8 + outer * 4));
        if (dataOffset == 0 || dataOffset > (uint)(store.Length - 6))
            return 0;

        // ItemVariationData: itemCount (2), wordDeltaCount (2), regionIndexCount (2),
        // regionIndexes (2 each), then itemCount rows.
        var data = store.Slice((int)dataOffset);
        int itemCount = BinaryPrimitives.ReadUInt16BigEndian(data);
        int wordDeltaCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));
        int columns = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
        var longWords = (wordDeltaCount & 0x8000) != 0;
        var wordColumns = wordDeltaCount & 0x7FFF;
        if (inner >= itemCount || wordColumns > columns)
            return 0;

        // A row holds the "word" columns first, then the short ones: int16 then int8, or with
        // LONG_WORDS int32 then int16.
        var shortSize = longWords ? 2 : 1;
        var rowBytes = (wordColumns * 2 + (columns - wordColumns)) * shortSize;
        var at = 6 + columns * 2L + (long)inner * rowBytes;
        if (at + rowBytes > data.Length)
            return 0;

        cost += columns;
        var total = 0.0;
        var row = data.Slice((int)at, rowBytes);
        var position = 0;
        for (var column = 0; column < columns; column++)
        {
            int delta;
            if (column < wordColumns)
            {
                delta = longWords
                    ? BinaryPrimitives.ReadInt32BigEndian(row.Slice(position))
                    : BinaryPrimitives.ReadInt16BigEndian(row.Slice(position));
                position += shortSize * 2;
            }
            else
            {
                delta = longWords ? BinaryPrimitives.ReadInt16BigEndian(row.Slice(position)) : (sbyte)row[position];
                position += shortSize;
            }

            int region = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6 + column * 2));
            if (region < _regionScalars.Length)
                total += delta * _regionScalars[region];
        }

        return total;
    }

    /// <summary>
    /// How strongly a region applies along one axis (all F2DOT14): 1 at the peak, falling
    /// linearly to 0 at start and end. A malformed range, or a zero peak, leaves the axis out.
    /// </summary>
    private static double AxisScalar(int start, int peak, int end, int coordinate)
    {
        if (start > peak || peak > end || peak == 0 || (start < 0 && end > 0))
            return 1;
        if (coordinate < start || coordinate > end)
            return 0;
        if (coordinate == peak)
            return 1;

        return coordinate < peak
            ? (double)(coordinate - start) / (peak - start)
            : (double)(end - coordinate) / (end - peak);
    }

    /// <summary>
    /// One normalized F2DOT14 coordinate per axis, per the OpenType overview's "Coordinate scales
    /// and normalization": 16.16 arithmetic, clamping to [-1, 1], the <c>avar</c> segment maps,
    /// then rounding to 2.14. Axes without a requested value, and NaN values, stay at the default.
    /// </summary>
    internal static int[] NormalizeCoordinates(
        IReadOnlyList<VariationAxis> axes, IReadOnlyDictionary<string, float> userValues, ReadOnlySpan<byte> avar)
    {
        var normalized = new int[axes.Count];
        for (var i = 0; i < normalized.Length; i++)
        {
            var axis = axes[i];
            if (!userValues.TryGetValue(axis.Tag, out var user) || float.IsNaN(user))
                continue;

            var min = ToFixed(axis.MinValue);
            var def = ToFixed(axis.DefaultValue);
            var max = ToFixed(axis.MaxValue);

            // Clamped in user space first: a value past +/-32768 overflows the 16.16 conversion.
            var clampedUser = Math.Clamp(
                (double)user,
                Math.Min(axis.MinValue, axis.DefaultValue),
                Math.Max(axis.MaxValue, axis.DefaultValue));
            var value = Math.Clamp(ToFixed(clampedUser), Math.Min(min, def), Math.Max(max, def));

            if (value < def)
                normalized[i] = -FixedDivide(def - value, def - min);
            else if (value > def)
                normalized[i] = FixedDivide(value - def, max - def);

            normalized[i] = Math.Clamp(normalized[i], -One, One);
        }

        ApplyAvar(avar, normalized);

        // 16.16 -> 2.14: add 2, then an arithmetic (sign-extending) shift right by 2.
        for (var i = 0; i < normalized.Length; i++)
            normalized[i] = (Math.Clamp(normalized[i], -One, One) + 2) >> 2;
        return normalized;
    }

    /// <summary>
    /// Remaps 16.16 coordinates through the version 1 segment maps of <paramref name="avar"/>,
    /// in one pass over the table. An axis the table does not reach, or whose map lacks the
    /// mandatory -1, 0 and 1 entries, is left as it is.
    /// </summary>
    private static void ApplyAvar(ReadOnlySpan<byte> avar, int[] normalized)
    {
        // majorVersion (2), minorVersion (2), reserved (2), axisCount (2), then per axis a
        // positionMapCount (2) and that many (from, to) F2DOT14 pairs.
        if (avar.Length < 8)
            return;

        var axisCount = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(avar.Slice(6)), normalized.Length);
        var at = 8;
        var map = new List<(int From, int To)>();
        for (var axis = 0; axis < axisCount; axis++)
        {
            if (at + 2 > avar.Length)
                return;
            int count = BinaryPrimitives.ReadUInt16BigEndian(avar.Slice(at));
            at += 2;
            if (at + count * 4L > avar.Length)
                return;

            var value = normalized[axis];
            if (value != 0)
            {
                map.Clear();
                for (var i = 0; i < count; i++)
                {
                    var from = BinaryPrimitives.ReadInt16BigEndian(avar.Slice(at + i * 4)) << 2;
                    var to = BinaryPrimitives.ReadInt16BigEndian(avar.Slice(at + i * 4 + 2)) << 2;

                    // Out-of-order records are dropped, which keeps the map monotonic.
                    if (map.Count > 0 && (from <= map[^1].From || to < map[^1].To))
                        continue;
                    map.Add((from, to));
                }

                if (map.Contains((-One, -One)) && map.Contains((0, 0)) && map.Contains((One, One)))
                    normalized[axis] = MapSegment(map, value);
            }

            at += count * 4;
        }
    }

    private static int MapSegment(List<(int From, int To)> map, int value)
    {
        // The first record whose from-coordinate reaches the value ends the segment; the -1
        // entry guarantees that is never the first record for an in-range value.
        for (var i = 1; i < map.Count; i++)
        {
            var end = map[i];
            if (end.From < value)
                continue;
            if (end.From == value)
                return end.To;

            var start = map[i - 1];
            long span = end.From - start.From;
            long offset = value - start.From;
            return start.To + (int)Math.Round((double)offset * (end.To - start.To) / span, MidpointRounding.AwayFromZero);
        }

        return map[^1].To;
    }

    /// <summary>A user value in 16.16: half a step or more rounds up, less truncates.</summary>
    private static int ToFixed(double value) => (int)Math.Floor((value * One) + 0.5);

    /// <summary>A 16.16 quotient rounded to the nearest step; 0 for a zero divisor.</summary>
    private static int FixedDivide(long numerator, long denominator) =>
        denominator == 0 ? 0 : (int)Math.Round((double)numerator * One / denominator, MidpointRounding.AwayFromZero);
}
