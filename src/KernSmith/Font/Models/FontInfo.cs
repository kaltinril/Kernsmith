using KernSmith.Font.Tables;

namespace KernSmith.Font.Models;

public sealed class FontInfo
{
    public required string FamilyName { get; init; }
    public required string StyleName { get; init; }
    public required int UnitsPerEm { get; init; }
    public required int Ascender { get; init; }
    public required int Descender { get; init; }
    public required int LineGap { get; init; }
    public int LineHeight => Ascender - Descender + LineGap;
    public required bool IsBold { get; init; }
    public required bool IsItalic { get; init; }
    public required bool IsFixedPitch { get; init; }
    public required int NumGlyphs { get; init; }
    public required IReadOnlyList<int> AvailableCodepoints { get; init; }
    public IReadOnlyList<KerningPair> KerningPairs { get; init; } = Array.Empty<KerningPair>();
    public Os2Metrics? Os2 { get; init; }
    public HeadTable? Head { get; init; }
    public HheaTable? Hhea { get; init; }
    public NameInfo? Names { get; init; }
    public IReadOnlyList<VariationAxis>? VariationAxes { get; init; }
    public IReadOnlyList<NamedInstance>? NamedInstances { get; init; }

    /// <summary>
    /// True if the font contains color glyph tables (COLR, sbix, or CBDT).
    /// </summary>
    public bool HasColorGlyphs { get; init; }
}
