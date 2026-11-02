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
/// places the ink by side, at full strength up to 16 ppem and fading to nothing by 40, and up to
/// 16 ppem rounds a blank glyph's advance up rather than to nearest (<see cref="BlankAdvance"/>),
/// so a space stays a word gap beside letters set in whole columns. Normal
/// also reads the face's <see cref="HintingProfile"/>: a bytecode face hinted for integer ppem
/// is fitted and rendered at <c>round(ppem)</c> with its linear advances rounded at that ppem,
/// and no face is fitted at a size its <c>gasp</c> says not to grid-fit — though such a size
/// still feeds the kerning bake.
/// </remarks>
internal sealed class GlyphHinter
{
    private readonly Func<int, int> _glyphIndexOf;
    private readonly Os2Table _os2;
    private readonly Func<int> _instanceStamp;
    private readonly bool _fixedPitch;
    private readonly HintingProfile _profile;
    private readonly Dictionary<CacheKey, ScaledHintMetrics?> _metrics = [];
    private readonly Dictionary<CacheKey, NormalHintMetrics?> _normalMetrics = [];
    private readonly Dictionary<CacheKey, float> _spaceAdvance = [];

    /// <summary>
    /// In bilevel at text sizes a blank glyph at least as wide as the space takes no fewer
    /// columns than this: a bilevel letter gap is one column, and a word gap must never be
    /// within 1.5 letter gaps of one.
    /// </summary>
    private const int MinWordGapColumns = 2;

    /// <summary>An advance within this of a whole pixel (1/64 px, FreeType's 26.6 resolution) rounds up to that pixel, not the next.</summary>
    private const float WholePixelTolerance = 1f / 64f;

    /// <summary>Creates the hinter for one face.</summary>
    /// <param name="glyphIndexOf">Maps a codepoint to a glyph index (0 when unmapped), to find reference glyphs.</param>
    /// <param name="os2">The face's <c>OS/2</c> table, the fallback for x-height and cap height.</param>
    /// <param name="instanceStamp">Identifies the current variation instance, so measurements
    /// taken at one instance are not reused at another.</param>
    /// <param name="fixedPitch">True for a fixed-pitch face (<see cref="IsFixedPitch"/>): its
    /// pairs are never padded or kerned by the hinter, since every advance is the pitch.</param>
    /// <param name="profile">What the face says about its hinting (<see cref="HintingProfile.For"/>);
    /// null for <see cref="HintingProfile.Autohint"/>, a face that says nothing.</param>
    public GlyphHinter(Func<int, int> glyphIndexOf, Os2Table os2, Func<int> instanceStamp, bool fixedPitch = false, HintingProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(glyphIndexOf);
        ArgumentNullException.ThrowIfNull(instanceStamp);
        _glyphIndexOf = glyphIndexOf;
        _os2 = os2;
        _instanceStamp = instanceStamp;
        _fixedPitch = fixedPitch;
        _profile = profile ?? HintingProfile.Autohint;
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
        if (EffectiveHintLevel(options) == HintLevel.Normal)
            return ApplyNormal(glyph, options, scale, unitsPerEm, glyphOf);

        if (glyph.IsBlank)
            return glyph;

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
    /// <remarks>
    /// What the face's <see cref="HintingProfile"/> changes, for a bytecode face hinted for
    /// integer ppem: the fit runs — and the glyph is rendered — at <c>round(ppem)</c>
    /// (<see cref="HintingProfile.HintPpem"/>), as FreeType's interpreter and GDI render it,
    /// so the designer's per-ppem x-height and cap rows come out; the advance is the linear
    /// advance at the integer ppem, rounded once, in both anti-alias modes — blank glyphs
    /// through <see cref="BlankAdvance"/>, since the space's advance sets word gaps — and never
    /// the face's <c>hdmx</c>
    /// record: that table holds the advances that go with the bytecode's own (for Georgia,
    /// widened) outlines, which Native does not draw, so with Native's ink it sets text loose.
    /// For every face, a size the
    /// <c>gasp</c> table says not to grid-fit is left unfitted (<see cref="PreparedGlyph.FitWithheld"/>),
    /// still at the integer ppem and with the chosen advance, and still with its spacing facts
    /// for the kerning bake. Past the taper nothing happens
    /// at all, so large text stays exactly unhinted, fractional ppem included.
    /// </remarks>
    private PreparedGlyph ApplyNormal(PreparedGlyph glyph, RasterOptions options, float scale, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf)
    {
        float ppem = scale * unitsPerEm;
        if (!_profile.PrefersIntegerPpem)
        {
            return glyph.IsBlank
                ? glyph with { Advance = BlankAdvance(glyph, options, glyph.AdvanceWidth * scale, glyph.Advance, ppem, glyphOf) }
                : FitNormal(glyph, options, scale, unitsPerEm, glyphOf, ppem, glyph.AdvanceWidth * scale);
        }

        // A face hinted for integer ppem: everything below runs at round(ppem), the
        // renderer's size stages included (PreparedGlyph.HintPpem), with the linear advance
        // at that ppem.
        float hintPpem = _profile.HintPpem(ppem);
        glyph = glyph with { HintPpem = hintPpem };
        if (NormalHinter.Strength(hintPpem) <= 0f)
            return glyph;

        // An integer request keeps the exact scale stage 4 used; a fractional one is rescaled.
        bool rescaled = !hintPpem.Equals(ppem);
        float hintScale = rescaled ? hintPpem / unitsPerEm : scale;
        float advance = glyph.AdvanceWidth * hintScale;
        if (glyph.IsBlank)
            return glyph with { Advance = BlankAdvance(glyph, options, advance, RoundAdvance(advance), hintPpem, glyphOf) };

        var metrics = NormalMetricsFor(options, hintScale, unitsPerEm, glyphOf);
        if (metrics is null)
            return Unfitted(glyph, hintScale, rescaled, advance, fitWithheld: false, spacing: null);
        if (!_profile.GridFitsAt(hintPpem))
            return Unfitted(glyph, hintScale, rescaled, advance, fitWithheld: true, WithheldSpacing(glyph.Outline, hintScale, advance, hintPpem, metrics));

        return FitNormal(glyph, options, hintScale, unitsPerEm, glyphOf, hintPpem, advance);
    }

    /// <summary>
    /// A blank glyph's advance under <c>Normal</c>. Up to the taper's start
    /// (<see cref="HintTuning.TaperStartPpem"/>) the linear advance rounds <em>up</em>, in both
    /// anti-alias modes, where every other glyph's rounds to nearest: the letters around a space
    /// sit in whole columns with a column of white between them, so a space rounded down is a
    /// word gap barely wider than a letter gap (Roboto at 9.1 ppem: 2.26 px → 2, "lazy dogs" set
    /// as "lazydogs" where the 'y' overhangs its advance; FreeType's bilevel line was 123 px to
    /// Native's 111). In bilevel a blank at least as wide as the space is also never under
    /// <see cref="MinWordGapColumns"/>. The design's own ratio of the space to a letter gap is no
    /// floor: on a serif face whose serifs nearly touch by design it is 8-9 (Georgia), and a
    /// bilevel letter gap cannot shrink below a column to match. Past the taper's start, and on
    /// a fixed-pitch face (every advance is the cell), <paramref name="rounded"/> stands.
    /// </summary>
    /// <param name="advance">The linear advance in pixels, unrounded, at the ppem the glyph is set at.</param>
    /// <param name="rounded">That advance rounded once to the nearest pixel, as the glyph would otherwise take it.</param>
    private int BlankAdvance(PreparedGlyph glyph, RasterOptions options, float advance, int rounded, float ppem, Func<int, DecodedGlyph?> glyphOf)
    {
        if (_fixedPitch || ppem > HintTuning.Current.TaperStartPpem)
            return rounded;

        int roundedUp = Math.Max(rounded, (int)MathF.Ceiling(advance - WholePixelTolerance));
        if (options.AntiAlias != AntiAliasMode.None || glyph.AdvanceWidth < SpaceAdvance(options, glyphOf))
            return roundedUp;

        return Math.Max(roundedUp, MinWordGapColumns);
    }

    /// <summary>The space's advance in font units after the request's transforms; infinite for a face without one, so no blank glyph counts as wider than it.</summary>
    private float SpaceAdvance(RasterOptions options, Func<int, DecodedGlyph?> glyphOf)
    {
        var key = new CacheKey(options.Size, options.Dpi, options.Bold, options.Italic, _instanceStamp());
        if (_spaceAdvance.TryGetValue(key, out float cached))
            return cached;

        int glyphIndex = _glyphIndexOf(' ');
        float advance = glyphIndex == 0 ? float.PositiveInfinity : glyphOf(glyphIndex)?.AdvanceWidth ?? float.PositiveInfinity;
        _spaceAdvance[key] = advance;
        return advance;
    }

    /// <summary>
    /// The spacing facts of a glyph whose fit the <c>gasp</c> table withheld, so the kerning
    /// bake still sees it: the glyph is set in whole columns like a fitted one and its touching
    /// pairs are as real (Verdana at 8 ppem: twelve lowercase pairs of a pangram). Null where
    /// no x fit would have run anyway — a face with no x axis to fit, or past the x taper.
    /// </summary>
    /// <param name="outline">The outline, in font units scaled by <paramref name="scale"/> to pixels.</param>
    /// <param name="advance">The glyph's advance in pixels, unrounded.</param>
    private static HintSpacing? WithheldSpacing(GlyphOutline outline, float scale, float advance, float ppem, NormalHintMetrics metrics)
    {
        if (!metrics.FitX || NormalHinter.XStrength(ppem) <= 0f)
            return null;

        var bounds = OutlineBounds.Tight(outline);
        return new HintSpacing(bounds.XMin * scale, advance - (bounds.XMax * scale), LeftStem: false, RightStem: false);
    }

    /// <summary>The fit itself, at a scale and ppem the caller chose, within <paramref name="advance"/> (unrounded pixels).</summary>
    private PreparedGlyph FitNormal(PreparedGlyph glyph, RasterOptions options, float scale, int unitsPerEm, Func<int, DecodedGlyph?> glyphOf, float ppem, float advance)
    {
        if (glyph.IsBlank)
            return glyph;

        var metrics = NormalMetricsFor(options, scale, unitsPerEm, glyphOf);
        if (metrics is null)
            return glyph;

        if (!_profile.GridFitsAt(ppem))
            return glyph with { FitWithheld = true, Spacing = WithheldSpacing(glyph.Outline, scale, advance, ppem, metrics) };

        bool mono = options.AntiAlias == AntiAliasMode.None;
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

    /// <summary>
    /// A glyph the <c>Normal</c> stage does not fit, for a face that prefers integer ppem:
    /// scaled to that ppem (its box from the scaled outline) with the chosen advance, so
    /// rendering and metrics agree with the fitted glyphs around it; with its spacing facts
    /// when the fit was withheld, so the kerning bake still pairs it.
    /// </summary>
    private static PreparedGlyph Unfitted(PreparedGlyph glyph, float hintScale, bool rescaled, float advance, bool fitWithheld, HintSpacing? spacing)
    {
        int rounded = RoundAdvance(advance);
        if (!rescaled)
            return glyph with { Advance = rounded, FitWithheld = fitWithheld, Spacing = spacing };

        var outline = GridMath.ScaleOutline(glyph.Outline, hintScale);
        return glyph with
        {
            Outline = outline,
            ToPixels = new OutlineTransform(1f, 0f),
            Box = GlyphBox.Compute(outline, null, 1f),
            Advance = rounded,
            FitWithheld = fitWithheld,
            Spacing = spacing,
        };
    }

    /// <summary>The advance rounded once to the nearest pixel, an exact half up (Native's advance rounding, as <see cref="NormalHinter"/> rounds it).</summary>
    private static int RoundAdvance(float advance) => (int)Math.Round(advance, MidpointRounding.AwayFromZero);

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
        var scaled = measured?.Scale(scale, unitsPerEm, xHeightRoundUp: _profile.XHeightRoundUp(HintTuning.Current));
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
