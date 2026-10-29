namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The <c>avar</c> (axis variations) table: per-axis piecewise-linear remapping of default
/// normalized coordinates.
/// </summary>
/// <remarks>
/// Only the version 1 segment maps are applied. A version 2 table starts with the same maps and
/// appends an axis-index map and an item variation store that mix axes into each other; that
/// extension is not implemented, so such a font gets its v1 mapping only.
/// </remarks>
internal sealed class AvarTable
{
    private const int One = 0x10000;

    /// <summary>Per axis, the usable (from, to) pairs in 16.16; empty means identity.</summary>
    private readonly (int From, int To)[][] _maps;

    private AvarTable((int From, int To)[][] maps) => _maps = maps;

    /// <summary>Parses the table's segment maps.</summary>
    /// <exception cref="FontFormatException">If the table is truncated.</exception>
    public static AvarTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);
        reader.Skip(2 + 2 + 2);     // majorVersion, minorVersion, reserved
        int axisCount = reader.ReadUInt16();

        var maps = new (int From, int To)[axisCount][];
        for (int axis = 0; axis < axisCount; axis++)
        {
            int count = reader.ReadUInt16();
            var pairs = new List<(int From, int To)>(count);
            for (int i = 0; i < count; i++)
            {
                int from = reader.ReadInt16() << 2;
                int to = reader.ReadInt16() << 2;

                // Records out of order — a from that does not increase, or a to that decreases —
                // may be ignored per the spec; dropping them keeps the map monotonic.
                if (pairs.Count > 0 && (from <= pairs[^1].From || to < pairs[^1].To))
                    continue;

                pairs.Add((from, to));
            }

            maps[axis] = HasRequiredMappings(pairs) ? pairs.ToArray() : [];
        }

        return new AvarTable(maps);
    }

    /// <summary>
    /// Remaps a default-normalized 16.16 coordinate for one axis. Axes the table does not cover,
    /// or whose map lacks the mandatory -1, 0 and 1 entries, pass through unchanged.
    /// </summary>
    public int Map(int axisIndex, int normalized)
    {
        if ((uint)axisIndex >= (uint)_maps.Length)
            return normalized;

        var map = _maps[axisIndex];
        if (map.Length == 0)
            return normalized;

        // The first record whose from-coordinate reaches the value ends the segment; the -1 entry
        // guarantees it is never the first record for an in-range value.
        for (int i = 1; i < map.Length; i++)
        {
            var end = map[i];
            if (end.From < normalized)
                continue;

            if (end.From == normalized)
                return end.To;

            var start = map[i - 1];
            long span = end.From - start.From;
            long offset = normalized - start.From;
            return start.To + (int)Math.Round((double)offset * (end.To - start.To) / span, MidpointRounding.AwayFromZero);
        }

        return map[^1].To;
    }

    private static bool HasRequiredMappings(List<(int From, int To)> pairs) =>
        pairs.Contains((-One, -One)) && pairs.Contains((0, 0)) && pairs.Contains((One, One));
}
