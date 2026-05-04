using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// Intermediate result of the rasterization phase, before atlas packing.
/// </summary>
internal sealed class RasterizationResult
{
    public required FontInfo FontInfo { get; init; }
    public required List<RasterizedGlyph> Glyphs { get; init; }
    public required List<int> Codepoints { get; init; }
    public required List<int> FailedCodepoints { get; init; }
    public required FontGeneratorOptions Options { get; init; }

    /// <summary>
    /// The effective ppem used for rasterization. When cell-height scaling is applied
    /// (default BMFont behavior), this differs from <see cref="Options"/>.Size.
    /// </summary>
    public float EffectiveSize { get; init; }

    /// <summary>
    /// Rasterizer-provided font-wide metrics, or null to fall back to TTF table calculation.
    /// </summary>
    public RasterizerFontMetrics? RasterizerFontMetrics { get; init; }

    /// <summary>
    /// Rasterizer-provided pre-scaled kerning pairs, or null to fall back to TTF GPOS/kern table parser.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? RasterizerKerningPairs { get; init; }
}
