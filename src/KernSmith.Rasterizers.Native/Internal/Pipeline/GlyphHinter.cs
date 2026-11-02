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
/// <see cref="EffectiveHintLevel"/> maps a request to the hinter that runs: the y-only fitter at
/// <see cref="HintLevel.Light"/>, the both-axis <see cref="NormalHinter"/> at
/// <see cref="HintLevel.Normal"/> — which is also what <see cref="HintLevel.Default"/> means
/// with <see cref="RasterOptions.EnableHinting"/> on, in every anti-alias mode — and nothing at
/// <see cref="HintLevel.None"/> or with hinting off. Light never fits x, so its advances, side
/// bearings and horizontal extents are exactly the unhinted ones; Normal keeps the advances and
/// places the ink by side, at full strength up to 16 ppem and fading to nothing by 40.
/// </remarks>
internal sealed class GlyphHinter
{
    private readonly Func<int, int> _glyphIndexOf;
    private readonly Os2Table _os2;
    private readonly Func<int> _instanceStamp;
    private readonly bool _fixedPitch;
    private readonly Dictionary<CacheKey, ScaledHintMetrics?> _metrics = [];
    private readonly Dictionary<CacheKey, NormalHintMetrics?> _normalMetrics = [];

    /// <summary>Creates the hinter for one face.</summary>
    /// <param name="glyphIndexOf">Maps a codepoint to a glyph index (0 when unmapped), to find reference glyphs.</param>
    /// <param name="os2">The face's <c>OS/2</c> table, the fallback for x-height and cap height.</param>
    /// <param name="instanceStamp">Identifies the current variation instance, so measurements
    /// taken at one instance are not reused at another; constant for a non-variable face.</param>
    /// <param name="fixedPitch">True for a fixed-pitch face (<see cref="IsFixedPitch"/>): its
    /// pairs are never padded or kerned by the hinter, since every advance is the pitch.</param>
    public GlyphHinter(Func<int, int> glyphIndexOf, Os2Table os2, Func<int> instanceStamp, bool fixedPitch = false)
    {
        ArgumentNullException.ThrowIfNull(glyphIndexOf);
        ArgumentNullException.ThrowIfNull(instanceStamp);
        _glyphIndexOf = glyphIndexOf;
        _os2 = os2;
        _instanceStamp = instanceStamp;
        _fixedPitch = fixedPitch;
    }

    /// <summary>
    /// The level Native hints a request at — the one switch point from the public options to
    /// the hinter that runs. An explicit <see cref="RasterOptions.HintLevel"/> wins outright
    /// (core rejects it combined with <see cref="RasterOptions.EnableHinting"/> false before it
    /// gets here); <see cref="HintLevel.Default"/> is <see cref="HintLevel.Normal"/> with
    /// <see cref="RasterOptions.EnableHinting"/> on (the owner's choice: small text is hinted
    /// unless asked otherwise, and the hinter's own size taper leaves large text alone) and
    /// <see cref="HintLevel.None"/> with it off, whatever the anti-alias mode —
    /// <see cref="AntiAliasMode.Light"/> no longer selects the y-only fitter by itself.
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
            _ => options.EnableHinting ? HintLevel.Normal : HintLevel.None,
        };
    }

    /// <summary>
    /// The options a color glyph's layers are prepared with: a request that would hint at
    /// <see cref="HintLevel.Normal"/> prepares them at <see cref="HintLevel.Light"/> instead,
    /// since Normal chooses each glyph's stem phase and x placement on its own and layers fitted
    /// one by one would no longer register on each other, where Light fits y only, against the
    /// zones every layer shares — and only while Normal itself would run (below its taper's
    /// end), so large color glyphs stay exactly unhinted like large plain ones. Light and None
    /// pass through.
    /// </summary>
    public static RasterOptions ForColorLayers(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (EffectiveHintLevel(options) != HintLevel.Normal)
            return options;

        return options with { HintLevel = NormalHinter.Strength(options.Size * options.Dpi / 72f) > 0f ? HintLevel.Light : HintLevel.None };
    }

    /// <summary>
    /// True when the request folds the <c>Normal</c> fit's pair collisions into the kerning
    /// pairs (<see cref="RasterOptions.BakeHintingIntoKerning"/>): an explicit setting wins,
    /// and unset it is on exactly when the request hints at <see cref="HintLevel.Normal"/>
    /// (<see cref="Applies"/>) — the pair is the only place a per-pair correction can live
    /// once advances are the font's, so a hinted request bakes unless told not to.
    /// </summary>
    public static bool BakesKerning(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Applies(options) && EffectiveHintLevel(options) == HintLevel.Normal
            && (options.BakeHintingIntoKerning ?? true);
    }

    /// <summary>
    /// True when a face is fixed pitch: it says so (<c>post.isFixedPitch</c>), or at least
    /// <see cref="FixedPitchShare"/> of its mapped printable ASCII glyphs share one font-unit
    /// advance — fonts that forget the flag still set in columns, and their advances must not
    /// be rebuilt.
    /// </summary>
    /// <param name="flagged">The <c>post</c> table's flag.</param>
    /// <param name="asciiAdvances">The font-unit advances of the mapped printable ASCII glyphs.</param>
    public static bool IsFixedPitch(bool flagged, IEnumerable<int> asciiAdvances)
    {
        ArgumentNullException.ThrowIfNull(asciiAdvances);
        if (flagged)
            return true;

        var counts = new Dictionary<int, int>();
        int total = 0;
        foreach (int advance in asciiAdvances)
        {
            counts[advance] = counts.GetValueOrDefault(advance) + 1;
            total++;
        }

        return total > 0 && counts.Values.Max() >= FixedPitchShare * total;
    }

    /// <summary>The share of printable ASCII glyphs that must share one advance for an unflagged face to count as fixed pitch.</summary>
    private const float FixedPitchShare = 0.95f;

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
    /// <param name="glyphOf">Stages 1–2 for another glyph index (decode and the request's
    /// transforms, font units: outline and advance), used to measure reference glyphs; null for
    /// a malformed glyph.</param>
    public PreparedGlyph Apply(PreparedGlyph glyph, RasterOptions options, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(glyphOf);

        if (!Applies(options))
            return glyph;

        float scale = glyph.ToPixels.Scale;
        if (glyph.IsBlank)
            return glyph;

        if (EffectiveHintLevel(options) == HintLevel.Normal)
            return ApplyNormal(glyph, options, scale, unitsPerEm, glyphOf);

        var metrics = MetricsFor(options, scale, unitsPerEm, glyphOf);
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
    /// The <see cref="HintLevel.Normal"/> stage: both axes fitted and the ink placed by side
    /// within the font's advance, rounded once (so tabular digits and fixed-pitch faces keep
    /// their columns by construction). Bilevel requests (<see cref="AntiAliasMode.None"/>) are
    /// fitted before the renderer's threshold, with whole-pixel stems. Without the kerning bake
    /// a right-hand stem with under half a pixel of white gets a column of advance; see
    /// <see cref="NormalHinter"/>.
    /// </summary>
    private PreparedGlyph ApplyNormal(PreparedGlyph glyph, RasterOptions options, float scale, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf)
    {
        var metrics = NormalMetricsFor(options, scale, unitsPerEm, glyphOf);
        if (metrics is null)
            return glyph;

        bool mono = options.AntiAlias == AntiAliasMode.None;
        float advance = glyph.AdvanceWidth * scale;
        var result = NormalHinter.Fit(GridMath.ScaleOutline(glyph.Outline, scale), advance, metrics, mono,
            padStemPairs: !BakesKerning(options) && !_fixedPitch);
        if (result is not { } fitted)
            return glyph;

        return glyph with
        {
            Outline = fitted.Outline,
            ToPixels = new OutlineTransform(1f, 0f),
            Box = GlyphBox.Compute(fitted.Outline, null, 1f),
            Advance = fitted.Advance,
            Spacing = fitted.XFitted ? new HintSpacing(fitted.FontLeft, fitted.FontRight, fitted.LeftStem, fitted.RightStem) : null,
        };
    }

    private ScaledHintMetrics? MetricsFor(RasterOptions options, float scale, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf)
    {
        // Synthetic bold moves the zones (and its strength depends on the size), so the style
        // and size are part of the key along with the variation instance.
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_metrics.TryGetValue(key, out var cached))
            return cached;

        var measured = FontHintMetrics.Measure(ReferenceOutlines(glyphOf), _os2, unitsPerEm);
        var scaled = measured?.Scale(scale, unitsPerEm);
        _metrics[key] = scaled;
        return scaled;
    }

    private NormalHintMetrics? NormalMetricsFor(RasterOptions options, float scale, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf)
    {
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_normalMetrics.TryGetValue(key, out var cached))
            return cached;

        var measured = NormalFontMetrics.Measure(ReferenceOutlines(glyphOf), _os2, unitsPerEm);
        var scaled = measured?.Scale(scale, unitsPerEm);
        _normalMetrics[key] = scaled;
        return scaled;
    }

    private Func<int, GlyphOutline?> ReferenceOutlines(Func<int, DecodedGlyph?> glyphOf) =>
        codepoint =>
        {
            int glyphIndex = _glyphIndexOf(codepoint);
            return glyphIndex == 0 ? null : glyphOf(glyphIndex)?.Outline;
        };

    private readonly record struct CacheKey(float Size, int Dpi, bool Bold, bool Italic, int Instance);
}
