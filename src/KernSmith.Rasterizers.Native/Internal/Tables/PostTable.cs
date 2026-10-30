namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>post</c> table header — the fields every version shares. The glyph-name data
/// that follows in versions 1, 2 and 2.5 is not read.
/// </summary>
/// <param name="Version">The table version as 16.16 fixed bits (0x00030000 for 3.0).</param>
/// <param name="ItalicAngle">Degrees counter-clockwise from vertical; negative for type that
/// leans right.</param>
/// <param name="UnderlinePosition">Top of the underline relative to the baseline, in font units.</param>
/// <param name="UnderlineThickness">Underline thickness, in font units.</param>
/// <param name="IsFixedPitch">True when the font is monospaced.</param>
internal readonly record struct PostTable(
    uint Version,
    float ItalicAngle,
    short UnderlinePosition,
    short UnderlineThickness,
    bool IsFixedPitch)
{
    /// <summary>Parses the <c>post</c> table header from its raw bytes.</summary>
    /// <exception cref="FontFormatException">If the table is shorter than its 16-byte header.</exception>
    public static PostTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        uint version = reader.ReadUInt32();
        float italicAngle = reader.ReadFixed();
        short underlinePosition = reader.ReadFWord();
        short underlineThickness = reader.ReadFWord();
        uint isFixedPitch = reader.ReadUInt32();

        return new PostTable(version, italicAngle, underlinePosition, underlineThickness, isFixedPitch != 0);
    }
}
