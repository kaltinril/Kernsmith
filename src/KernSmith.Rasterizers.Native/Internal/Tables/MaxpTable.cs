namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>maxp</c> (maximum profile) table. <c>numGlyphs</c> sizes the <c>hmtx</c>
/// and <c>loca</c> tables; the v1.0 extended profile fields bound glyph decoding — most
/// importantly <c>maxComponentDepth</c>, which caps composite glyph recursion.
/// </summary>
/// <remarks>
/// CFF fonts carry a v0.5 (<c>0x00005000</c>) table that stops after <c>numGlyphs</c>.
/// For those the extended fields are left at zero; see <see cref="ComponentDepthLimit"/>
/// for the fallback used when no depth is declared.
/// </remarks>
internal readonly record struct MaxpTable(
    ushort NumGlyphs,
    ushort MaxPoints,
    ushort MaxContours,
    ushort MaxCompositePoints,
    ushort MaxCompositeContours,
    ushort MaxComponentElements,
    ushort MaxComponentDepth)
{
    /// <summary>Hard ceiling on composite recursion, regardless of what the font declares.</summary>
    public const int MaxAllowedComponentDepth = 64;

    /// <summary>
    /// The composite recursion limit to enforce: the font's declared
    /// <c>maxComponentDepth</c> when it is present and sane, otherwise
    /// <see cref="MaxAllowedComponentDepth"/>. A font cannot raise the cap.
    /// </summary>
    public int ComponentDepthLimit =>
        MaxComponentDepth is > 0 and <= MaxAllowedComponentDepth
            ? MaxComponentDepth
            : MaxAllowedComponentDepth;

    /// <summary>Parses the <c>maxp</c> table from its raw bytes.</summary>
    public static MaxpTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        // version (Fixed, 4 bytes) precedes numGlyphs in both v0.5 and v1.0.
        uint version = reader.ReadUInt32();
        ushort numGlyphs = reader.ReadUInt16();

        // v0.5 (0x00005000) ends here. Only v1.0 (0x00010000) carries the extended profile,
        // and a truncated v1.0 table is treated the same way rather than throwing — nothing
        // downstream requires these fields to be present.
        const int ExtendedProfileBytes = 26;
        if (version < 0x00010000 || reader.Remaining < ExtendedProfileBytes)
            return new MaxpTable(numGlyphs, 0, 0, 0, 0, 0, 0);

        ushort maxPoints = reader.ReadUInt16();
        ushort maxContours = reader.ReadUInt16();
        ushort maxCompositePoints = reader.ReadUInt16();
        ushort maxCompositeContours = reader.ReadUInt16();

        // maxZones(2) maxTwilightPoints(2) maxStorage(2) maxFunctionDefs(2)
        // maxInstructionDefs(2) maxStackElements(2) maxSizeOfInstructions(2) => 14 bytes.
        // All are hinting-interpreter limits; we do not run hinting.
        reader.Skip(14);

        ushort maxComponentElements = reader.ReadUInt16();
        ushort maxComponentDepth = reader.ReadUInt16();

        return new MaxpTable(
            numGlyphs,
            maxPoints,
            maxContours,
            maxCompositePoints,
            maxCompositeContours,
            maxComponentElements,
            maxComponentDepth);
    }
}
