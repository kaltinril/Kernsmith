namespace Bmfontier.Font.Tables;

public sealed record HheaTable(
    int Ascender,
    int Descender,
    int LineGap,
    int AdvanceWidthMax,
    int NumberOfHMetrics);
