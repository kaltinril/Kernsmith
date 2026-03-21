namespace KernSmith.Rasterizer;

/// <summary>
/// Rasterizes font glyphs into bitmaps for texture atlas packing.
/// </summary>
public interface IRasterizer : IDisposable
{
    /// <summary>
    /// Loads a font from raw file bytes for subsequent rasterization.
    /// </summary>
    /// <param name="fontData">The font file bytes.</param>
    /// <param name="faceIndex">Which face to use in a .ttc font collection. Usually 0.</param>
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);

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
}
