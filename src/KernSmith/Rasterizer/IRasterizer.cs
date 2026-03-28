using KernSmith.Font.Tables;

namespace KernSmith.Rasterizer;

/// <summary>
/// Rasterizes font glyphs into bitmaps for texture atlas packing.
/// </summary>
public interface IRasterizer : IDisposable
{
    /// <summary>
    /// Describes what this rasterizer backend supports.
    /// </summary>
    IRasterizerCapabilities Capabilities { get; }

    /// <summary>
    /// Loads a font from raw file bytes for subsequent rasterization.
    /// </summary>
    /// <param name="fontData">The font file bytes.</param>
    /// <param name="faceIndex">Which face to use in a .ttc font collection. Usually 0.</param>
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);

    /// <summary>
    /// Loads a system-installed font by family name for subsequent rasterization.
    /// Not all backends support this — the default throws <see cref="NotSupportedException"/>.
    /// Check <see cref="IRasterizerCapabilities.SupportsSystemFonts"/> before calling.
    /// </summary>
    /// <param name="familyName">The font family name (e.g., "Arial").</param>
    void LoadSystemFont(string familyName) => throw new NotSupportedException(
        "Rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");

    /// <summary>
    /// Renders a single character to a bitmap. Returns null if the glyph is missing from the font.
    /// </summary>
    /// <param name="codepoint">The Unicode character code to render.</param>
    /// <param name="options">Size, DPI, anti-aliasing, and other rendering settings.</param>
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);

    /// <summary>
    /// Renders multiple characters to bitmaps, skipping any that are missing from the font.
    /// </summary>
    /// <param name="codepoints">The Unicode character codes to render.</param>
    /// <param name="options">Size, DPI, anti-aliasing, and other rendering settings.</param>
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);

    /// <summary>
    /// Gets glyph metrics without rasterizing. Returns null if the glyph is missing from the font.
    /// </summary>
    /// <param name="codepoint">The Unicode character code.</param>
    /// <param name="options">Size, DPI, and other settings.</param>
    Font.Models.GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) => null;

    /// <summary>
    /// Returns rasterizer-provided font-wide metrics. Returns null to fall back to TTF table calculation.
    /// </summary>
    RasterizerFontMetrics? GetFontMetrics(RasterOptions options) => null;

    /// <summary>
    /// Returns rasterizer-provided kerning pairs already scaled to pixel values.
    /// Returns null to fall back to TTF GPOS/kern table parser.
    /// </summary>
    IReadOnlyList<Font.Models.ScaledKerningPair>? GetKerningPairs(RasterOptions options) => null;

    /// <summary>
    /// Applies variable font axis values. Only called when
    /// <see cref="IRasterizerCapabilities.SupportsVariableFonts"/> is true.
    /// </summary>
    /// <param name="fvarAxes">The axes defined in the font's fvar table, in order.</param>
    /// <param name="userAxes">User-specified axis tag/value pairs (e.g., "wght" = 700).</param>
    void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes) { }

    /// <summary>
    /// Selects a color palette by index for color font rendering. Only called when
    /// <see cref="IRasterizerCapabilities.SupportsColorFonts"/> is true.
    /// </summary>
    /// <param name="paletteIndex">Zero-based palette index.</param>
    void SelectColorPalette(int paletteIndex) { }
}
