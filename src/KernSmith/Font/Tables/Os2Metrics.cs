namespace KernSmith.Font.Tables;

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
    int LastCharIndex);
