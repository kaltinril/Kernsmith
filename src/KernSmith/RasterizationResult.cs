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

    /// <summary>The super-sample level the glyphs were rendered at (1 for LCD), the <c>.fnt</c> <c>aa</c>.</summary>
    public int SuperSampleLevel { get; init; } = 1;

    /// <summary>
    /// Rasterizer-provided font-wide metrics, or null to fall back to TTF table calculation.
    /// </summary>
    public RasterizerFontMetrics? RasterizerFontMetrics { get; init; }

    /// <summary>
    /// Rasterizer-provided pre-scaled kerning pairs, or null to fall back to TTF GPOS/kern table parser.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? RasterizerKerningPairs { get; init; }

    /// <summary>
    /// Snapshot of the glyphs after height-stretch/custom-glyph overrides and any
    /// super-sample downscale, but BEFORE outline/gradient/shadow effects were composited.
    /// Only populated when <see cref="FontGeneratorOptions.Variants"/> is non-empty — atlas
    /// variants (e.g. a shadow silhouette) need the glyph's own bare coverage, not a copy that
    /// may already have a baked shadow/outline/gradient composited into its RGBA.
    /// </summary>
    public List<RasterizedGlyph>? RawGlyphsForVariants { get; init; }

    /// <summary>The span every cell was equalized to, or null when equalization is off.</summary>
    public EqualizedSpan? EqualizedSpan { get; init; }

    /// <summary>
    /// Backend-drawn outline layers by codepoint, at the output size and placed against the final
    /// <see cref="Glyphs"/>, for an Outline channel to use instead of the bitmap outline. Only
    /// populated for <see cref="OutlineMode.Vector"/> with a channel layout that routes the
    /// outline; a glyph missing here falls back to the bitmap outline.
    /// </summary>
    public IReadOnlyDictionary<int, OutlineMask>? ChannelOutlineMasks { get; init; }
}
