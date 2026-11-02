using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// A rasterized glyph: bitmap pixel data with positioning metrics.
/// </summary>
public sealed class RasterizedGlyph
{
    /// <summary>The Unicode character code this glyph represents.</summary>
    public required int Codepoint { get; init; }

    /// <summary>Internal glyph index inside the font file.</summary>
    public required int GlyphIndex { get; init; }

    /// <summary>The pixel data. Grayscale (1 byte/pixel) or RGBA (4 bytes/pixel) depending on <see cref="Format"/>.</summary>
    public required byte[] BitmapData { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Bytes per row in <see cref="BitmapData"/> (may include padding).</summary>
    public required int Pitch { get; init; }

    /// <summary>Glyph positioning metrics: bearing offsets, advance width, and rendered dimensions.</summary>
    public required GlyphMetrics Metrics { get; init; }

    /// <summary>Whether the pixel data is grayscale or RGBA.</summary>
    public required PixelFormat Format { get; init; }

    /// <summary>
    /// The glyph's advance and left side bearing before rounding, and what hinting moved them by,
    /// when the backend can supply them and <see cref="RasterOptions.FractionalMetrics"/> asked
    /// for them; null otherwise (the pipeline then falls back to the font's own design metrics
    /// where it can). Read by the pipeline straight after rasterization, so a post-processor
    /// that rebuilds the glyph need not carry it.
    /// </summary>
    public FractionalGlyphMetrics? Fractional { get; init; }
}
