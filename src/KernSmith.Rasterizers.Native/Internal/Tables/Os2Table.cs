namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>OS/2</c> table. Provides the typographic and Windows ascent/descent
/// metrics plus the x-height and cap-height used by downstream metric and small-caps
/// phases. <see cref="XHeight"/> and <see cref="CapHeight"/> were added in table
/// version 2 and are reported as zero for older fonts that omit them.
/// </summary>
internal readonly record struct Os2Table(
    ushort Version,
    short TypoAscender,
    short TypoDescender,
    ushort WinAscent,
    ushort WinDescent,
    short XHeight,
    short CapHeight)
{
    /// <summary>Parses the <c>OS/2</c> table from its raw bytes.</summary>
    public static Os2Table Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        ushort version = reader.ReadUInt16();

        // Field layout, in bytes from the start of the table (per the OpenType spec):
        //   sTypoAscender @ 68, sTypoDescender @ 70, sTypoLineGap @ 72,
        //   usWinAscent @ 74, usWinDescent @ 76,
        //   sxHeight @ 86, sCapHeight @ 88 (version >= 2 only).
        // We have consumed 2 bytes (version); skip 66 more to reach offset 68.
        reader.Skip(66);
        short typoAscender = reader.ReadInt16();   // @68
        short typoDescender = reader.ReadInt16();  // @70

        reader.Skip(2);                            // sTypoLineGap @72
        ushort winAscent = reader.ReadUInt16();    // @74
        ushort winDescent = reader.ReadUInt16();   // @76

        short xHeight = 0;
        short capHeight = 0;
        if (version >= 2)
        {
            // After usWinDescent we are at offset 78; sxHeight lives at offset 86.
            // ulCodePageRange1(4) + ulCodePageRange2(4) fill the gap.
            reader.Skip(8);
            xHeight = reader.ReadInt16();          // @86
            capHeight = reader.ReadInt16();        // @88
        }

        return new Os2Table(version, typoAscender, typoDescender, winAscent, winDescent, xHeight, capHeight);
    }
}
