namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// The parsed <c>head</c> (font header) table. Provides the units-per-em scale, the
/// <c>loca</c> index format, the mac style flags, and the global glyph bounding box.
/// </summary>
internal readonly record struct HeadTable(
    ushort UnitsPerEm,
    short IndexToLocFormat,
    ushort MacStyle,
    short XMin,
    short YMin,
    short XMax,
    short YMax)
{
    /// <summary>True when the <c>macStyle</c> bold bit is set.</summary>
    public bool IsBold => (MacStyle & 0x0001) != 0;

    /// <summary>True when the <c>macStyle</c> italic bit is set.</summary>
    public bool IsItalic => (MacStyle & 0x0002) != 0;

    /// <summary>True when glyph offsets in <c>loca</c> use the long (32-bit) format.</summary>
    public bool LongLocaFormat => IndexToLocFormat == 1;

    /// <summary>Parses the <c>head</c> table from its raw bytes.</summary>
    public static HeadTable Parse(ReadOnlySpan<byte> data)
    {
        var reader = new FontReader(data);

        // majorVersion(2) minorVersion(2) fontRevision(4) checkSumAdjustment(4)
        // magicNumber(4) flags(2) => 18 bytes before unitsPerEm.
        reader.Skip(18);
        ushort unitsPerEm = reader.ReadUInt16();

        // created(8) modified(8) => 16 bytes.
        reader.Skip(16);
        short xMin = reader.ReadInt16();
        short yMin = reader.ReadInt16();
        short xMax = reader.ReadInt16();
        short yMax = reader.ReadInt16();
        ushort macStyle = reader.ReadUInt16();

        // lowestRecPPEM(2) fontDirectionHint(2) => 4 bytes before indexToLocFormat.
        reader.Skip(4);
        short indexToLocFormat = reader.ReadInt16();

        if (unitsPerEm == 0)
            throw new FontFormatException("head", 18, "unitsPerEm is zero.");

        return new HeadTable(unitsPerEm, indexToLocFormat, macStyle, xMin, yMin, xMax, yMax);
    }
}
