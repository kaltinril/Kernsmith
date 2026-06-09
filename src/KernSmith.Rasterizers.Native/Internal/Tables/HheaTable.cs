namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>hhea</c> (horizontal header) table. Provides vertical font metrics
/// and the count of long horizontal metrics in the <c>hmtx</c> table.
/// </summary>
internal readonly record struct HheaTable(
    short Ascender,
    short Descender,
    short LineGap,
    ushort NumberOfHMetrics)
{
    /// <summary>Parses the <c>hhea</c> table from its raw bytes.</summary>
    public static HheaTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        // majorVersion(2) minorVersion(2) => 4 bytes before ascender.
        reader.Skip(4);
        short ascender = reader.ReadInt16();
        short descender = reader.ReadInt16();
        short lineGap = reader.ReadInt16();

        // advanceWidthMax(2) minLeftSideBearing(2) minRightSideBearing(2) xMaxExtent(2)
        // caretSlopeRise(2) caretSlopeRun(2) caretOffset(2) reserved x4 (8)
        // metricDataFormat(2) => 24 bytes before numberOfHMetrics.
        reader.Skip(24);
        ushort numberOfHMetrics = reader.ReadUInt16();

        return new HheaTable(ascender, descender, lineGap, numberOfHMetrics);
    }
}
