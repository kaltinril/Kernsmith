namespace KernSmith.Font.Tables;

/// <summary>
/// Data from the font's 'OS/2' table: weight, width, and platform-specific metrics.
/// </summary>
/// <param name="WeightClass">Visual weight class. 400 = regular, 700 = bold.</param>
/// <param name="WidthClass">Visual width class. 5 = normal width.</param>
/// <param name="TypoAscender">Typographic ascent above the baseline, in font units.</param>
/// <param name="TypoDescender">Typographic descent below the baseline, in font units (usually negative).</param>
/// <param name="TypoLineGap">Typographic line gap, in font units.</param>
/// <param name="WinAscent">Windows clipping ascent boundary, in font units.</param>
/// <param name="WinDescent">Windows clipping descent boundary, in font units.</param>
/// <param name="XHeight">Height of lowercase 'x', in font units.</param>
/// <param name="CapHeight">Height of uppercase letters, in font units.</param>
/// <param name="Panose">10-byte array classifying the font's visual style (serif, weight, etc.).</param>
/// <param name="FirstCharIndex">Lowest Unicode character in the font.</param>
/// <param name="LastCharIndex">Highest Unicode character in the font.</param>
/// <param name="XAvgCharWidth">Weighted average width of lowercase Latin letters and space, in font units.</param>
/// <param name="SubscriptXSize">Horizontal size for subscripts, in font units.</param>
/// <param name="SubscriptYSize">Vertical size for subscripts, in font units.</param>
/// <param name="SuperscriptXSize">Horizontal size for superscripts, in font units.</param>
/// <param name="SuperscriptYSize">Vertical size for superscripts, in font units.</param>
/// <param name="StrikeoutSize">Width of the strikeout stroke, in font units.</param>
/// <param name="StrikeoutPosition">Position of the strikeout stroke relative to the baseline, in font units.</param>
public sealed record Os2Metrics(
    int WeightClass,
    int WidthClass,
    int TypoAscender,
    int TypoDescender,
    int TypoLineGap,
    int WinAscent,
    int WinDescent,
    int XHeight,
    int CapHeight,
    byte[] Panose,
    int FirstCharIndex,
    int LastCharIndex,
    short XAvgCharWidth = 0,
    short SubscriptXSize = 0,
    short SubscriptYSize = 0,
    short SuperscriptXSize = 0,
    short SuperscriptYSize = 0,
    short StrikeoutSize = 0,
    short StrikeoutPosition = 0);
