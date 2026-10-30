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

    /// <summary>
    /// Whether the rasterizer draws outline layers itself: true means it implements
    /// <see cref="IRasterizer.RasterizeOutlineLayer"/>, which the pipeline then uses for
    /// <see cref="OutlineMode.Vector"/> outlines. False keeps the pipeline's bitmap outline.
    /// </summary>
    bool SupportsOutlineStroke { get; }

    /// <summary>
    /// The anti-alias modes this rasterizer supports. The generation pipeline never hands the
    /// rasterizer a mode missing from this list, except <see cref="AntiAliasMode.None"/>: an
    /// unlisted <see cref="AntiAliasMode.Light"/> or <see cref="AntiAliasMode.Lcd"/> request
    /// arrives as <see cref="AntiAliasMode.Grayscale"/> when that is listed.
    /// </summary>
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

    /// <summary>
    /// When true, the rasterizer can render multi-channel distance fields
    /// (<see cref="SdfMode.Msdf"/> and <see cref="SdfMode.Mtsdf"/>) as RGBA glyphs.
    /// </summary>
    bool SupportsMsdf => false;

    /// <summary>
    /// When true, the rasterizer honors <see cref="RasterOptions.WidthPercent"/> (synthetic
    /// condensed/expanded outlines and advances).
    /// </summary>
    bool SupportsWidthScale => false;

    /// <summary>
    /// When true, the rasterizer slants synthetic italic by <see cref="RasterOptions.ItalicAngle"/>
    /// rather than an angle of its own.
    /// </summary>
    bool SupportsItalicAngle => false;
}
