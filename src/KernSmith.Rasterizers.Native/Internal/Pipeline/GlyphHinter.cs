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
/// Runs when <see cref="EffectiveHintLevel"/> is <see cref="HintLevel.Light"/>: an explicit
/// <see cref="RasterOptions.HintLevel"/> of Light (whatever the anti-alias mode), or, at
/// <see cref="HintLevel.Default"/>, <see cref="AntiAliasMode.Light"/> with
/// <see cref="RasterOptions.EnableHinting"/> on — light hinting, which is what y-only fitting is.
/// Every other request, the default (grayscale with <see cref="RasterOptions.EnableHinting"/>
/// true) included, passes through unchanged, so default output does not move. X is never fitted,
/// so advances, side bearings and the bitmap's horizontal extent are exactly the unhinted ones.
/// </remarks>
internal sealed class GlyphHinter
{
    private readonly Func<int, int> _glyphIndexOf;
    private readonly Os2Table _os2;
    private readonly Func<int> _instanceStamp;
    private readonly Dictionary<CacheKey, ScaledHintMetrics?> _metrics = [];
    private readonly Dictionary<CacheKey, NormalHintMetrics?> _normalMetrics = [];

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

    /// <summary>
    /// The level Native hints a request at — the one switch point from the public options to
    /// the hinter that runs. An explicit <see cref="RasterOptions.HintLevel"/> wins outright
    /// (core rejects it combined with <see cref="RasterOptions.EnableHinting"/> false before it
    /// gets here); <see cref="HintLevel.Default"/> is the original mapping, hinted only for
    /// <see cref="AntiAliasMode.Light"/> with hinting on, which keeps default output unchanged.
    /// <see cref="HintLevel.Normal"/> is the both-axis auto-hinter (<see cref="NormalHinter"/>);
    /// <see cref="HintLevel.Full"/> runs as Normal until a font-native engine exists — the
    /// pipeline never sends it (<c>NativeCapabilities.SupportedHintLevels</c>), so that only
    /// covers direct callers.
    /// </summary>
    /// <returns><see cref="HintLevel.None"/>, <see cref="HintLevel.Light"/> or <see cref="HintLevel.Normal"/>, never Default.</returns>
    public static HintLevel EffectiveHintLevel(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.HintLevel switch
        {
            HintLevel.None => HintLevel.None,
            HintLevel.Light => HintLevel.Light,
            HintLevel.Normal or HintLevel.Full => HintLevel.Normal,
            _ => options.EnableHinting && options.AntiAlias == AntiAliasMode.Light ? HintLevel.Light : HintLevel.None,
        };
    }

    /// <summary>
    /// True when a request is hinted: <see cref="EffectiveHintLevel"/> is not
    /// <see cref="HintLevel.None"/>, and the output is neither a distance field (SDF, MSDF or
    /// MTSDF), which is resampled at any scale and must not be snapped to one grid, nor LCD,
    /// whose hinting is not implemented yet.
    /// </summary>
    public static bool Applies(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return EffectiveHintLevel(options) != HintLevel.None
            && options.EffectiveSdfMode == SdfMode.None
            && options.AntiAlias != AntiAliasMode.Lcd;
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
        if (EffectiveHintLevel(options) == HintLevel.Normal)
            return ApplyNormal(glyph, options, scale, unitsPerEm, shapeOf);

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

    /// <summary>
    /// The <see cref="HintLevel.Normal"/> stage: both axes fitted and the advance rebuilt, so
    /// the result's <see cref="PreparedGlyph.Advance"/> can differ from the rounded linear one.
    /// Mono requests (<see cref="AntiAliasMode.None"/>) are fitted before the renderer's
    /// threshold, with whole-pixel stems.
    /// </summary>
    private PreparedGlyph ApplyNormal(PreparedGlyph glyph, RasterOptions options, float scale, int unitsPerEm, Func<int, GlyphOutline?> shapeOf)
    {
        var metrics = NormalMetricsFor(options, scale, unitsPerEm, shapeOf);
        if (metrics is null)
            return glyph;

        var result = NormalHinter.Fit(
            GridMath.ScaleOutline(glyph.Outline, scale),
            glyph.AdvanceWidth * scale,
            metrics,
            mono: options.AntiAlias == AntiAliasMode.None);
        if (result is not { } fitted)
            return glyph;

        return glyph with
        {
            Outline = fitted.Outline,
            ToPixels = new OutlineTransform(1f, 0f),
            Box = GlyphBox.Compute(fitted.Outline, null, 1f),
            Advance = fitted.Advance,
        };
    }

    private ScaledHintMetrics? MetricsFor(RasterOptions options, float scale, int unitsPerEm, Func<int, GlyphOutline?> shapeOf)
    {
        // Synthetic bold moves the zones (and its strength depends on the size), so the style
        // and size are part of the key along with the variation instance.
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_metrics.TryGetValue(key, out var cached))
            return cached;

        var measured = FontHintMetrics.Measure(ReferenceOutlines(shapeOf), _os2, unitsPerEm);
        var scaled = measured?.Scale(scale, unitsPerEm);
        _metrics[key] = scaled;
        return scaled;
    }

    private NormalHintMetrics? NormalMetricsFor(RasterOptions options, float scale, int unitsPerEm, Func<int, GlyphOutline?> shapeOf)
    {
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_normalMetrics.TryGetValue(key, out var cached))
            return cached;

        var measured = NormalFontMetrics.Measure(ReferenceOutlines(shapeOf), _os2, unitsPerEm);
        var scaled = measured?.Scale(scale, unitsPerEm);
        _normalMetrics[key] = scaled;
        return scaled;
    }

    private Func<int, GlyphOutline?> ReferenceOutlines(Func<int, GlyphOutline?> shapeOf) =>
        codepoint =>
        {
            int glyphIndex = _glyphIndexOf(codepoint);
            return glyphIndex == 0 ? null : shapeOf(glyphIndex);
        };

    private readonly record struct CacheKey(float Size, int Dpi, bool Bold, bool Italic, int Instance);
}
