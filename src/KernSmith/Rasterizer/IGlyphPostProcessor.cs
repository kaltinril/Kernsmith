namespace KernSmith.Rasterizer;

/// <summary>
/// Post-processes a rasterized glyph bitmap, applying effects such as outline, shadow, or gradient.
/// </summary>
public interface IGlyphPostProcessor
{
    /// <summary>
    /// Takes a rendered glyph and returns a new one with the effect applied.
    /// </summary>
    /// <param name="glyph">The glyph to modify.</param>
    RasterizedGlyph Process(RasterizedGlyph glyph);
}
