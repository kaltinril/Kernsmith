namespace KernSmith.Font.Tables;

/// <summary>
/// Data from the font's 'head' table: global metrics and bounding box for all glyphs.
/// </summary>
/// <param name="UnitsPerEm">Design units per em square (typically 1000 or 2048).</param>
/// <param name="XMin">Left edge of the bounding box that fits all glyphs.</param>
/// <param name="YMin">Bottom edge of the bounding box that fits all glyphs.</param>
/// <param name="XMax">Right edge of the bounding box that fits all glyphs.</param>
/// <param name="YMax">Top edge of the bounding box that fits all glyphs.</param>
/// <param name="IndexToLocFormat">How glyph offsets are stored: 0 = short (16-bit), 1 = long (32-bit).</param>
/// <param name="Created">When the font was created (as a raw timestamp).</param>
/// <param name="Modified">When the font was last modified (as a raw timestamp).</param>
public sealed record HeadTable(
    int UnitsPerEm,
    int XMin,
    int YMin,
    int XMax,
    int YMax,
    int IndexToLocFormat,
    long Created,
    long Modified);
