namespace KernSmith.Rasterizer;

/// <summary>
/// Describes what a rasterizer backend supports.
/// </summary>
public interface IRasterizerCapabilities
{
    /// <summary>Whether the rasterizer can render color font glyphs (COLR/CPAL/sbix/CBDT).</summary>
    bool SupportsColorFonts { get; }

    /// <summary>Whether the rasterizer can apply variable font axis coordinates.</summary>
    bool SupportsVariableFonts { get; }

    /// <summary>Whether the rasterizer can produce signed distance field output.</summary>
    bool SupportsSdf { get; }

    /// <summary>Whether the rasterizer can stroke glyph outlines.</summary>
    bool SupportsOutlineStroke { get; }

    /// <summary>The anti-alias modes this rasterizer supports.</summary>
    IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; }

    /// <summary>
    /// When true, the rasterizer handles font sizing internally and BmFont should
    /// pass the raw fontSize without cell-height-to-ppem conversion.
    /// </summary>
    bool HandlesOwnSizing => false;

    /// <summary>
    /// When true, the rasterizer can load system-installed fonts by family name
    /// via <see cref="IRasterizer.LoadSystemFont"/> instead of requiring font bytes.
    /// </summary>
    bool SupportsSystemFonts => false;

    /// <summary>
    /// When true, the rasterizer can apply synthetic bold (emboldening) to glyph outlines.
    /// </summary>
    bool SupportsSyntheticBold => false;

    /// <summary>
    /// When true, the rasterizer can apply synthetic italic (oblique/shear) to glyph outlines.
    /// </summary>
    bool SupportsSyntheticItalic => false;
}
