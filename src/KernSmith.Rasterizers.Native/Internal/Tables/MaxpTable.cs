namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>maxp</c> (maximum profile) table. Only <c>numGlyphs</c> is needed at
/// this phase — it sizes the <c>hmtx</c> and <c>loca</c> tables. The remaining profile
/// fields are deferred until glyph decoding (Phase 162).
/// </summary>
internal readonly record struct MaxpTable(ushort NumGlyphs)
{
    /// <summary>Parses the <c>maxp</c> table from its raw bytes.</summary>
    public static MaxpTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        // version (Fixed, 4 bytes) precedes numGlyphs in both v0.5 and v1.0.
        reader.Skip(4);
        ushort numGlyphs = reader.ReadUInt16();

        return new MaxpTable(numGlyphs);
    }
}
