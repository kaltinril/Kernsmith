using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates a visual layer from a grayscale glyph alpha mask.
/// Unlike IGlyphPostProcessor (linear chain), effects generate layers
/// independently and are composited in a fixed back-to-front order.
/// </summary>
internal interface IGlyphEffect
{
    /// <summary>The compositing order. Lower values render further back.</summary>
    int ZOrder { get; }

    /// <summary>
    /// How far the layer <see cref="Generate"/> returns reaches past the source glyph on each
    /// side, in pixels (<see cref="BmFont.QueryAtlasSize(byte[], FontGeneratorOptions?)"/> sizes
    /// cells from it). Keep in sync with <see cref="Generate"/>.
    /// </summary>
    (int Left, int Top, int Right, int Bottom) Margins => default;

    /// <summary>Generate a layer from the source glyph's alpha data.</summary>
    GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics);
}

/// <summary>
/// A visual layer produced by an IGlyphEffect, ready for compositing.
/// </summary>
internal sealed record GlyphLayer(
    byte[] RgbaData,
    int Width,
    int Height,
    int OffsetX,   // offset relative to original glyph origin
    int OffsetY,   // offset relative to original glyph origin
    int ZOrder);
