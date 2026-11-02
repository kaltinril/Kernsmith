namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>The rendering a <c>gasp</c> range asks for; the <c>rangeGaspBehavior</c> flag bits.</summary>
[Flags]
internal enum GaspBehavior : ushort
{
    /// <summary>No flag set.</summary>
    None = 0,

    /// <summary>Grid-fit (hint) at these sizes.</summary>
    GridFit = 0x0001,

    /// <summary>Render with grayscale anti-aliasing at these sizes.</summary>
    DoGray = 0x0002,

    /// <summary>Grid-fit with ClearType symmetric smoothing (version 1 tables).</summary>
    SymmetricGridFit = 0x0004,

    /// <summary>ClearType symmetric smoothing (version 1 tables).</summary>
    SymmetricSmoothing = 0x0008,
}

/// <summary>One <c>gasp</c> range: the behavior for every ppem up to and including <paramref name="MaxPpem"/> that no lower range covers.</summary>
internal readonly record struct GaspRange(int MaxPpem, GaspBehavior Behavior);

/// <summary>
/// The parsed <c>gasp</c> (grid-fitting and scan-conversion procedure) table: the designer's
/// statement of which ppem ranges to grid-fit and which to anti-alias. The common Microsoft
/// shape is "no grid-fitting at 8 ppem and under, bilevel grid-fitting to 16 or 17, everything
/// above".
/// </summary>
/// <remarks>
/// Layout: <c>version</c> (uint16, 0 or 1), <c>numRanges</c> (uint16), then <c>numRanges</c>
/// pairs of <c>rangeMaxPPEM</c> (uint16) and <c>rangeGaspBehavior</c> (uint16), sorted by
/// <c>rangeMaxPPEM</c> with a last range of 0xFFFF. Lookups do not rely on the order. The
/// table is optional and advisory, so a malformed one is ignored (<see cref="TryParse"/> returns
/// null) rather than failing the font.
/// </remarks>
internal sealed class GaspTable
{
    private const int HeaderSize = 4;
    private const int RangeSize = 4;

    private readonly GaspRange[] _ranges;

    private GaspTable(GaspRange[] ranges)
    {
        _ranges = ranges;
    }

    /// <summary>The ranges as stored.</summary>
    public IReadOnlyList<GaspRange> Ranges => _ranges;

    /// <summary>
    /// The behavior for an integer ppem: that of the range with the smallest
    /// <see cref="GaspRange.MaxPpem"/> at or above it.
    /// </summary>
    /// <returns>Null when no range reaches the ppem (a table without the 0xFFFF sentinel).</returns>
    public GaspBehavior? BehaviorAt(int ppem)
    {
        GaspRange? best = null;
        foreach (var range in _ranges)
        {
            if (range.MaxPpem >= ppem && (best is null || range.MaxPpem < best.Value.MaxPpem))
                best = range;
        }

        return best?.Behavior;
    }

    /// <summary>
    /// True when the designer asks for grid-fitting at an integer ppem (<see cref="GaspBehavior.GridFit"/>
    /// or <see cref="GaspBehavior.SymmetricGridFit"/>), false when the covering range asks for
    /// none, null when no range covers the ppem.
    /// </summary>
    public bool? GridFitsAt(int ppem) =>
        BehaviorAt(ppem) is { } behavior ? (behavior & (GaspBehavior.GridFit | GaspBehavior.SymmetricGridFit)) != 0 : null;

    /// <summary>
    /// Parses the table; null when it is malformed — too short for its header or for the
    /// ranges it declares — or declares no range.
    /// </summary>
    public static GaspTable? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            return null;

        var reader = new FontReader(data);
        reader.ReadUInt16(); // version: 0 or 1; version 1 only adds the symmetric flag bits, read the same way.
        int numRanges = reader.ReadUInt16();
        if (numRanges == 0 || reader.Remaining < numRanges * RangeSize)
            return null;

        var ranges = new GaspRange[numRanges];
        for (int i = 0; i < numRanges; i++)
            ranges[i] = new GaspRange(reader.ReadUInt16(), (GaspBehavior)reader.ReadUInt16());

        return new GaspTable(ranges);
    }
}
