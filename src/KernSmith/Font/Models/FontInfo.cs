using KernSmith.Font.Tables;

namespace KernSmith.Font.Models;

/// <summary>
/// Parsed font metadata: family name, style, vertical metrics, kerning pairs, and OpenType table data.
/// </summary>
public sealed class FontInfo
{
    /// <summary>Font family name, like "Roboto" or "Arial".</summary>
    public required string FamilyName { get; init; }

    /// <summary>Style name, like "Regular", "Bold Italic".</summary>
    public required string StyleName { get; init; }

    /// <summary>Design units per em square. Typically 1000 or 2048.</summary>
    public required int UnitsPerEm { get; init; }

    /// <summary>Maximum ascent above the baseline, in font units.</summary>
    public required int Ascender { get; init; }

    /// <summary>Maximum descent below the baseline, in font units (usually negative).</summary>
    public required int Descender { get; init; }

    /// <summary>Additional inter-line spacing, in font units.</summary>
    public required int LineGap { get; init; }

    /// <summary>Total line height: Ascender - Descender + LineGap.</summary>
    public int LineHeight => Ascender - Descender + LineGap;

    /// <summary>True if this is a bold font.</summary>
    public required bool IsBold { get; init; }

    /// <summary>True if this is an italic font.</summary>
    public required bool IsItalic { get; init; }

    /// <summary>True if the font is monospaced (all characters have the same advance width).</summary>
    public required bool IsFixedPitch { get; init; }

    /// <summary>Total number of glyphs in the font file.</summary>
    public required int NumGlyphs { get; init; }

    /// <summary>All Unicode characters this font can render.</summary>
    public required IReadOnlyList<int> AvailableCodepoints { get; init; }

    /// <summary>Kerning pairs (spacing tweaks between specific character pairs like "AV").</summary>
    public IReadOnlyList<KerningPair> KerningPairs { get; init; } = Array.Empty<KerningPair>();

    /// <summary>OS/2 table data (weight, width, platform-specific metrics). Null if the table is missing.</summary>
    public Os2Metrics? Os2 { get; init; }

    /// <summary>Font header table (units-per-em, bounding box, timestamps). Null if missing.</summary>
    public HeadTable? Head { get; init; }

    /// <summary>Horizontal header (ascender, descender, line gap from hhea). Null if missing.</summary>
    public HheaTable? Hhea { get; init; }

    /// <summary>Name table strings (family, copyright, PostScript name). Null if missing.</summary>
    public NameInfo? Names { get; init; }

    /// <summary>Variable font axes like weight and width. Null for non-variable fonts.</summary>
    public IReadOnlyList<VariationAxis>? VariationAxes { get; init; }

    /// <summary>Preset variable font styles like "Bold" or "Light". Null for non-variable fonts.</summary>
    public IReadOnlyList<NamedInstance>? NamedInstances { get; init; }

    /// <summary>True if the font has color glyphs (emoji, color layers via COLR/sbix/CBDT).</summary>
    public bool HasColorGlyphs { get; init; }
}
