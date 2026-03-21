namespace KernSmith.Font.Tables;

/// <summary>
/// Data from the font's 'hhea' table: vertical extents and horizontal spacing limits.
/// </summary>
/// <param name="Ascender">Maximum ascent above the baseline, in font units.</param>
/// <param name="Descender">Maximum descent below the baseline, in font units (usually negative).</param>
/// <param name="LineGap">Additional inter-line spacing, in font units.</param>
/// <param name="AdvanceWidthMax">Maximum advance width across all glyphs, in font units.</param>
/// <param name="NumberOfHMetrics">Number of entries in the horizontal metrics (hmtx) table.</param>
public sealed record HheaTable(
    int Ascender,
    int Descender,
    int LineGap,
    int AdvanceWidthMax,
    int NumberOfHMetrics);
