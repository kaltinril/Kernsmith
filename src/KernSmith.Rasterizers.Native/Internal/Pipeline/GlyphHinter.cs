using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The pixel-space hint stage at the end of stage 4: Native's own auto-hinter (Phase 174).
/// Vertical-only grid fitting (<see cref="GridFitter"/>) against blue zones and a standard
/// stroke thickness measured once per face, size, style and variation instance
/// (<see cref="FontHintMetrics"/>).
/// </summary>
/// <remarks>
/// Runs only for <see cref="AntiAliasMode.Light"/> with <see cref="RasterOptions.EnableHinting"/>
/// on — light hinting, which is what y-only fitting is. Every other request, the default
/// (grayscale with <see cref="RasterOptions.EnableHinting"/> true) included, passes through
/// unchanged, so default output does not move. X is never fitted, so advances, side bearings
/// and the bitmap's horizontal extent are exactly the unhinted ones.
/// </remarks>
internal sealed class GlyphHinter
{
    private readonly Func<int, int> _glyphIndexOf;
    private readonly Os2Table _os2;
    private readonly Func<int> _instanceStamp;
    private readonly Dictionary<CacheKey, ScaledHintMetrics?> _metrics = [];

    /// <summary>Creates the hinter for one face.</summary>
    /// <param name="glyphIndexOf">Maps a codepoint to a glyph index (0 when unmapped), to find reference glyphs.</param>
    /// <param name="os2">The face's <c>OS/2</c> table, the fallback for x-height and cap height.</param>
    /// <param name="instanceStamp">Identifies the current variation instance, so measurements
    /// taken at one instance are not reused at another; constant for a non-variable face.</param>
    public GlyphHinter(Func<int, int> glyphIndexOf, Os2Table os2, Func<int> instanceStamp)
    {
        ArgumentNullException.ThrowIfNull(glyphIndexOf);
        ArgumentNullException.ThrowIfNull(instanceStamp);
        _glyphIndexOf = glyphIndexOf;
        _os2 = os2;
        _instanceStamp = instanceStamp;
    }

    /// <summary>True when a request is hinted: light anti-aliasing with hinting enabled.</summary>
    public static bool Applies(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.EnableHinting && options.AntiAlias == AntiAliasMode.Light && !options.Sdf;
    }

    /// <summary>
    /// Returns the glyph to render: unchanged when the request is not hinted, the font cannot
    /// be measured, or nothing in the glyph needed fitting; otherwise a pixel-space outline
    /// (Y up, identity <see cref="PreparedGlyph.ToPixels"/>: scale 1, baseline 0) with its box
    /// recomputed from the fitted outline. Built with <c>with</c>, so
    /// <see cref="PreparedGlyph.GlyphIndex"/> and the advance carry over.
    /// </summary>
    /// <param name="glyph">The scaled glyph.</param>
    /// <param name="options">The request.</param>
    /// <param name="unitsPerEm">The face's <c>head.unitsPerEm</c>.</param>
    /// <param name="shapeOf">Stages 1–2 for another glyph index (decode and the request's
    /// transforms, font units), used to measure reference glyphs; null for a malformed glyph.</param>
    public PreparedGlyph Apply(PreparedGlyph glyph, RasterOptions options, int unitsPerEm, Func<int, GlyphOutline?> shapeOf)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(shapeOf);

        if (!Applies(options) || glyph.IsBlank)
            return glyph;

        float scale = glyph.ToPixels.Scale;
        var metrics = MetricsFor(options, scale, unitsPerEm, shapeOf);
        if (metrics is null)
            return glyph;

        var fitted = GridFitter.Fit(GridMath.ScaleOutline(glyph.Outline, scale), metrics);
        if (fitted is null)
            return glyph;

        return glyph with
        {
            Outline = fitted,
            ToPixels = new OutlineTransform(1f, 0f),
            Box = GlyphBox.Compute(fitted, null, 1f),
        };
    }

    private ScaledHintMetrics? MetricsFor(RasterOptions options, float scale, int unitsPerEm, Func<int, GlyphOutline?> shapeOf)
    {
        // Synthetic bold moves the zones (and its strength depends on the size), so the style
        // and size are part of the key along with the variation instance.
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_metrics.TryGetValue(key, out var cached))
            return cached;

        var measured = FontHintMetrics.Measure(
            codepoint =>
            {
                int glyphIndex = _glyphIndexOf(codepoint);
                return glyphIndex == 0 ? null : shapeOf(glyphIndex);
            },
            _os2,
            unitsPerEm);

        var scaled = measured?.Scale(scale, unitsPerEm);
        _metrics[key] = scaled;
        return scaled;
    }

    private readonly record struct CacheKey(float Size, int Dpi, bool Bold, bool Italic, int Instance);
}
