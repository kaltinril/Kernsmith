using System.Threading.Tasks;
using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// The one-time setup half of the glyph rasterization pipeline (phase 183): font decompression,
/// parse, rasterizer creation and font load, option resolution (effective ppem, render scale,
/// effects, post-processors). <see cref="RasterizeAndProcess"/> is the repeatable per-call half —
/// it rasterizes and processes a given codepoint batch without re-doing any of the setup, so an
/// incremental session can add glyphs cheaply. <see cref="BmFont.RasterizeFont"/> composes the
/// two for the classic one-shot path.
/// </summary>
internal sealed class PreparedRasterization : IDisposable
{
    /// <summary>
    /// Below this glyph count the per-glyph transform passes run on a plain loop — parallel
    /// dispatch overhead exceeds the work for the tiny batches an incremental session adds
    /// (commonly 1-2 glyphs).
    /// </summary>
    private const int ParallelThreshold = 8;

    private readonly IRasterizer _rasterizer;
    private readonly bool _ownsRasterizer;
    private readonly RasterOptions _rasterOptions;
    private readonly RasterOptions _effectiveRasterOptions;
    private readonly int _renderScale;
    private readonly List<IGlyphEffect> _effects;
    private readonly bool _hasEffects;
    private readonly List<IGlyphPostProcessor>? _activePostProcessors;
    private bool _disposed;

    // Rasterizer-provided metrics/kerning depend only on the fixed _rasterOptions, so
    // they are captured once and reused across RasterizeAndProcess calls — an
    // incremental session must not re-query the rasterizer on every add.
    private bool _rasterizerProvidedCaptured;
    private RasterizerFontMetrics? _rasterizerFontMetrics;
    private IReadOnlyList<ScaledKerningPair>? _rasterizerKerningPairs;

    /// <summary>Decompressed sfnt font bytes (WOFF/WOFF2 already expanded).</summary>
    public byte[] FontData { get; }

    /// <summary>Parsed font metadata. Subsetted to the character set or full, per the
    /// <c>subsetToCharacterSet</c> argument given to <see cref="Prepare"/>.</summary>
    public FontInfo FontInfo { get; }

    /// <summary>The original generation options this preparation was resolved from.</summary>
    public FontGeneratorOptions Options { get; }

    /// <summary>
    /// The effective ppem used for rasterization. When cell-height scaling is applied
    /// (default BMFont behavior), this differs from <see cref="Options"/>.Size.
    /// </summary>
    public float EffectiveSize { get; }

    private PreparedRasterization(
        byte[] fontData,
        FontInfo fontInfo,
        FontGeneratorOptions options,
        IRasterizer rasterizer,
        bool ownsRasterizer,
        RasterOptions rasterOptions,
        RasterOptions effectiveRasterOptions,
        float effectiveSize,
        int renderScale,
        List<IGlyphEffect> effects,
        bool hasEffects,
        List<IGlyphPostProcessor>? activePostProcessors)
    {
        FontData = fontData;
        FontInfo = fontInfo;
        Options = options;
        _rasterizer = rasterizer;
        _ownsRasterizer = ownsRasterizer;
        _rasterOptions = rasterOptions;
        _effectiveRasterOptions = effectiveRasterOptions;
        EffectiveSize = effectiveSize;
        _renderScale = renderScale;
        _effects = effects;
        _hasEffects = hasEffects;
        _activePostProcessors = activePostProcessors;
    }

    /// <summary>
    /// Performs all per-font setup: validation, WOFF decompression, font parse, rasterizer
    /// creation/load, synthetic-style gating, variation axes, color palette, effective-ppem
    /// resolution, render-scale sizing, and effect/post-processor list construction.
    /// </summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="options">Generation options.</param>
    /// <param name="systemFontName">System font family to load via the rasterizer's own system
    /// font path (when supported), or null to load <paramref name="fontData"/> directly.</param>
    /// <param name="subsetToCharacterSet">When true, the TTF parse filters the cmap (and via it
    /// the kern/GPOS pairs) to <see cref="FontGeneratorOptions.Characters"/> — the classic
    /// <c>Generate</c> behavior. When false, the font is parsed unfiltered so codepoints outside
    /// the initial character set stay resolvable and keep their kerning pairs (phase 183 F7) —
    /// required for incremental sessions that add characters later.</param>
    internal static PreparedRasterization Prepare(
        byte[] fontData, FontGeneratorOptions options, string? systemFontName, bool subsetToCharacterSet)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        if (options.Size <= 0 || options.Size > 10000)
            throw new ArgumentOutOfRangeException(nameof(options), $"Size must be between 1 and 10000, was {options.Size}.");

        // 0. Auto-detect and decompress WOFF/WOFF2 to standard sfnt
        if (WoffDecompressor.IsWoff(fontData) || WoffDecompressor.IsWoff2(fontData))
            fontData = WoffDecompressor.Decompress(fontData);

        // 0b. Guard: SDF + super sampling is invalid — box filter corrupts distance values.
        if (options.Sdf && options.SuperSampleLevel > 1)
            throw new InvalidOperationException(
                "SDF rendering cannot be combined with super sampling (SuperSampleLevel > 1). " +
                "The box-filter downscale corrupts signed distance field values.");

        // 1. Parse font
        var fontReader = options.FontReader ?? new TtfFontReader();

        if (fontReader is TtfFontReader ttfReader)
        {
            ttfReader.RequestedCodepoints = subsetToCharacterSet
                ? options.Characters.GetCodepointsHashSet()
                : null;
            ttfReader.SharedFontBytes = fontData;
        }

        var fontInfo = fontReader.ReadFont(fontData, options.FaceIndex);

        // 2. Create and configure the rasterizer
        var rasterizer = options.Rasterizer
            ?? RasterizerFactory.Create(options.Backend);
        var ownsRasterizer = options.Rasterizer == null;
        try
        {
            // Guard: channel packing is incompatible with color fonts.
            if (options.ChannelPacking && options.ColorFont)
                throw new InvalidOperationException(
                    "Channel packing and color font rendering cannot be used together. " +
                    "Color glyphs are RGBA and cannot be packed into individual channels.");

            // Guard: channel packing is incompatible with effects (outline, gradient, shadow).
            if (options.ChannelPacking && HasAnyEffects(options))
                throw new InvalidOperationException(
                    "Channel packing cannot be combined with effects (outline, gradient, shadow). " +
                    "Effects produce RGBA glyphs which cannot be packed into individual channels.");

            if (systemFontName is not null && rasterizer.Capabilities.SupportsSystemFonts)
                rasterizer.LoadSystemFont(systemFontName);
            else
                rasterizer.LoadFont(fontData, options.FaceIndex);

            // Guard: don't apply synthetic bold/italic on fonts that are already styled.
            // FreeType checks style_flags internally, but GDI and DirectWrite don't —
            // this ensures consistent behavior across all backends.
            // ForceSynthetic overrides: the user explicitly wants synthetic on top.
            var effectiveBold = options.Bold;
            var effectiveForceSyntheticBold = options.ForceSyntheticBold;
            var effectiveItalic = options.Italic;
            var effectiveForceSyntheticItalic = options.ForceSyntheticItalic;

            if (fontInfo.IsBold && effectiveBold && !effectiveForceSyntheticBold)
            {
                effectiveBold = false;
                effectiveForceSyntheticBold = false;
            }
            if (fontInfo.IsItalic && effectiveItalic && !effectiveForceSyntheticItalic)
            {
                effectiveItalic = false;
                effectiveForceSyntheticItalic = false;
            }

            if (rasterizer.Capabilities.SupportsVariableFonts
                && options.VariationAxes is { Count: > 0 }
                && fontInfo.VariationAxes is { Count: > 0 })
            {
                rasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes);
            }

            if (rasterizer.Capabilities.SupportsColorFonts
                && options.ColorFont && options.ColorPaletteIndex != 0)
            {
                rasterizer.SelectColorPalette(options.ColorPaletteIndex);
            }

            if (options.Sdf && !rasterizer.Capabilities.SupportsSdf)
            {
                throw new NotSupportedException(
                    $"Rasterizer backend does not support SDF rendering. " +
                    $"Use a backend that reports SupportsSdf = true (e.g., FreeType or StbTrueType).");
            }

            var rasterOptions = RasterOptions.FromGeneratorOptions(options) with
            {
                Bold = effectiveBold,
                ForceSyntheticBold = effectiveForceSyntheticBold,
                Italic = effectiveItalic,
                ForceSyntheticItalic = effectiveForceSyntheticItalic
            };

            // BMFont treats fontSize as cell height (usWinAscent + usWinDescent scaled),
            // not as em-square size (ppem). Compute the effective ppem that produces the
            // requested cell height. When MatchCharHeight is true (negative fontSize in
            // .bmfc), use fontSize directly as ppem (em-square mode).
            // When the rasterizer handles its own sizing (e.g., GDI), skip this conversion.
            float effectiveSize = options.Size;
            if (!rasterizer.Capabilities.HandlesOwnSizing)
            {
                if (!options.MatchCharHeight
                    && fontInfo.Os2 is { } os2CellScale
                    && os2CellScale.WinAscent + os2CellScale.WinDescent > 0)
                {
                    effectiveSize = (float)(
                        (double)options.Size * fontInfo.UnitsPerEm
                        / (os2CellScale.WinAscent + os2CellScale.WinDescent));
                    if (effectiveSize < 1f) effectiveSize = 1f;
                    rasterOptions = rasterOptions with { Size = effectiveSize };
                }
            }

            var ssLevel = Math.Clamp(options.SuperSampleLevel, 1, 4);

            // SDF supersampling: render the distance field at a larger ppem, then box-average
            // back down. Unlike alpha-coverage super sampling (forbidden with SDF above
            // because averaging coverage corrupts distances), a FreeType SDF byte is locally
            // linear in signed distance (128 = edge crossing), so box-averaging preserves the
            // edge zero-crossing — this is the standard "render SDF high-res, then downsample"
            // technique. SDF and SuperSampleLevel>1 are mutually exclusive (rejected above), so
            // renderScale collapses to a single upscale factor for both paths.
            var sdfScale = options.Sdf ? Math.Clamp(options.SdfScale, 1, 4) : 1;
            var renderScale = sdfScale > 1 ? sdfScale : ssLevel;
            var effectiveRasterOptions = renderScale > 1
                ? rasterOptions with { Size = rasterOptions.Size * renderScale }
                : rasterOptions;

            // Pre-compute transform parameters once
            var effects = BuildEffects(options);
            // A non-default (non-opaque-white) fill color must tint the body layer even when no
            // other effects are present, so it has to trigger compositing on its own.
            var hasFillTint = options.FillColorR != 255 || options.FillColorG != 255
                || options.FillColorB != 255 || options.FillColorA != 255;
            var hasEffects = effects.Count > 0 || hasFillTint;
            List<IGlyphPostProcessor>? activePostProcessors = null;
            if (options.PostProcessors != null)
            {
                foreach (var processor in options.PostProcessors)
                {
                    if (processor is OutlinePostProcessor or GradientPostProcessor or ShadowPostProcessor)
                        continue;

                    // Skip the dilation/shear fallback only when the backend really did apply the
                    // transform itself, or the glyph gets styled twice. A backend that reports
                    // false ignores Bold/Italic outright, so dropping the caller's post-processor
                    // there produced unstyled glyphs with no error (Phase 150 Issue 5).
                    if (processor is BoldPostProcessor && options.Bold
                        && rasterizer.Capabilities.SupportsSyntheticBold)
                        continue;
                    if (processor is ItalicPostProcessor && options.Italic
                        && rasterizer.Capabilities.SupportsSyntheticItalic)
                        continue;

                    activePostProcessors ??= [];
                    activePostProcessors.Add(processor);
                }
            }

            return new PreparedRasterization(
                fontData, fontInfo, options, rasterizer, ownsRasterizer,
                rasterOptions, effectiveRasterOptions, effectiveSize, renderScale,
                effects, hasEffects, activePostProcessors);
        }
        catch
        {
            if (ownsRasterizer)
                rasterizer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Resolves the codepoints to rasterize from <see cref="Options"/>.Characters against the
    /// parsed font's available codepoints, appending the fallback character/codepoint when
    /// configured (FallbackCodepoint takes precedence over FallbackCharacter).
    /// </summary>
    internal List<int> ResolveCodepoints()
    {
        var codepoints = Options.Characters.Resolve(FontInfo.AvailableCodepoints).ToList();

        // FallbackCodepoint takes precedence over FallbackCharacter
        var resolvedFallback = Options.FallbackCodepoint
            ?? (Options.FallbackCharacter.HasValue ? (int)Options.FallbackCharacter.Value : (int?)null);
        if (resolvedFallback.HasValue)
        {
            if (!codepoints.Contains(resolvedFallback.Value))
                codepoints.Add(resolvedFallback.Value);
        }

        return codepoints;
    }

    /// <summary>
    /// Rasterizes and processes the given codepoints through the prepared pipeline: rasterize,
    /// height-stretch, custom-glyph substitution, effects/post-processor/downscale pass, and
    /// cell-height equalization. Callable repeatedly on one prepared instance.
    /// </summary>
    /// <param name="codepoints">The codepoints to rasterize.</param>
    /// <param name="equalizeTargetHeight">When <see cref="FontGeneratorOptions.EqualizeCellHeights"/>
    /// is on: null pads every glyph to the batch's own max height (the classic behavior); a value
    /// pads to exactly that height instead, so glyphs added later match the cells of an existing
    /// equalized atlas (phase 183 F3). Throws <see cref="InvalidOperationException"/> if any glyph
    /// is taller than the given target. Ignored when equalization is off.</param>
    internal RasterizationResult RasterizeAndProcess(IReadOnlyList<int> codepoints, int? equalizeTargetHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var options = Options;
        var codepointList = codepoints as List<int> ?? codepoints.ToList();

        var glyphs = _rasterizer.RasterizeAll(codepointList, _effectiveRasterOptions).ToList();

        // HeightStretch first (before custom glyphs, matching original order)
        if (options.HeightPercent != 100)
        {
            var stretch = new HeightStretchPostProcessor(options.HeightPercent);
            for (var i = 0; i < glyphs.Count; i++)
                glyphs[i] = stretch.Process(glyphs[i]);
        }

        // Custom glyphs (after stretch, before effects - matching original order)
        if (options.CustomGlyphs is { Count: > 0 })
        {
            glyphs = ApplyCustomGlyphs(glyphs, options.CustomGlyphs, codepointList);
        }

        var needsDownscale = _renderScale > 1;

        // Atlas variants (e.g. a shadow silhouette) need the glyph's own bare coverage —
        // not a copy that may already have a baked outline/gradient/shadow composited into
        // its RGBA below — so snapshot the pre-effects glyph here (still applying the same
        // super-sample downscale as the primary, so scale matches).
        List<RasterizedGlyph>? rawGlyphsForVariants = options.Variants is { Count: > 0 }
            ? new List<RasterizedGlyph>(new RasterizedGlyph[glyphs.Count])
            : null;

        // Apply remaining per-glyph transforms in a single pass (parallelized on non-WASM,
        // above the small-batch threshold)
        if (_hasEffects || _activePostProcessors != null || needsDownscale || rawGlyphsForVariants != null)
        {
            void ProcessGlyph(int i)
            {
                var g = glyphs[i];
                if (rawGlyphsForVariants != null)
                    rawGlyphsForVariants[i] = needsDownscale ? SuperSampleDownscale(g, _renderScale) : g;
                if (_hasEffects) g = GlyphCompositor.Composite(g, _effects,
                    options.FillColorR, options.FillColorG, options.FillColorB, options.FillColorA);
                if (_activePostProcessors != null)
                {
                    foreach (var processor in _activePostProcessors)
                        g = processor.Process(g);
                }
                if (needsDownscale) g = SuperSampleDownscale(g, _renderScale);
                glyphs[i] = g;
            }

            if (OperatingSystem.IsBrowser() || glyphs.Count < ParallelThreshold)
            {
                for (var i = 0; i < glyphs.Count; i++)
                    ProcessGlyph(i);
            }
            else
            {
                Parallel.For(0, glyphs.Count, ProcessGlyph);
            }
        }

        if (options.EqualizeCellHeights && glyphs.Count > 0)
        {
            var targetHeight = equalizeTargetHeight ?? glyphs.Max(g => g.Height);
            if (equalizeTargetHeight.HasValue)
            {
                // Stable packing forbids changing existing cells: a glyph taller than the
                // atlas's equalized cell height would have grown every cell in the original
                // atlas, so it cannot be added incrementally.
                foreach (var g in glyphs)
                {
                    if (g.Height > targetHeight)
                        throw new InvalidOperationException(
                            $"Glyph U+{g.Codepoint:X4} is {g.Height}px tall, which exceeds the atlas's " +
                            $"equalized cell height of {targetHeight}px. Adding it would change the " +
                            "existing atlas's cells; the atlas must be regenerated to include this glyph.");
                }
            }
            if (OperatingSystem.IsBrowser() || glyphs.Count < ParallelThreshold)
            {
                for (var i = 0; i < glyphs.Count; i++)
                    glyphs[i] = EqualizeCellHeight(glyphs[i], targetHeight);
            }
            else
            {
                Parallel.For(0, glyphs.Count, i =>
                    glyphs[i] = EqualizeCellHeight(glyphs[i], targetHeight));
            }
        }

        // Phase 78F parity with BMFont: with an outline, contour-less glyphs (e.g. space)
        // become a (1 + 2*outline)^2 transparent cell so the atlas reserves space and the
        // .fnt has correct dims. Runs after equalization, matching where every caller
        // previously applied it.
        if (options.Outline > 0)
        {
            var outlineSize = 1 + options.Outline * 2;
            for (var i = 0; i < glyphs.Count; i++)
            {
                var g = glyphs[i];
                if (g.Width == 0 && g.Height == 0 && g.Metrics.Advance > 0)
                {
                    glyphs[i] = new RasterizedGlyph
                    {
                        Codepoint = g.Codepoint,
                        GlyphIndex = g.GlyphIndex,
                        Width = outlineSize,
                        Height = outlineSize,
                        Pitch = outlineSize,
                        BitmapData = new byte[outlineSize * outlineSize],
                        Metrics = g.Metrics,
                        Format = g.Format,
                    };
                }
            }
        }

        var rasterizedCodepoints = new HashSet<int>(glyphs.Select(g => g.Codepoint));
        var failedCodepoints = codepointList.Where(cp => !rasterizedCodepoints.Contains(cp)).ToList();

        // Capture rasterizer-provided values (once) so the result stays valid after disposal
        if (!_rasterizerProvidedCaptured)
        {
            _rasterizerFontMetrics = _rasterizer.GetFontMetrics(_rasterOptions);
            _rasterizerKerningPairs = _rasterizer.GetKerningPairs(_rasterOptions);
            _rasterizerProvidedCaptured = true;
        }

        return new RasterizationResult
        {
            FontInfo = FontInfo,
            Glyphs = glyphs,
            Codepoints = codepointList,
            FailedCodepoints = failedCodepoints,
            Options = options,
            EffectiveSize = EffectiveSize,
            RasterizerFontMetrics = _rasterizerFontMetrics,
            RasterizerKerningPairs = _rasterizerKerningPairs,
            RawGlyphsForVariants = rawGlyphsForVariants
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // Mirror RasterizeFont's ownership rule: a caller-injected rasterizer
        // (options.Rasterizer) is caller-owned and never disposed here.
        if (_ownsRasterizer)
            _rasterizer.Dispose();
    }

    /// <summary>Checks if any built-in effects (outline, gradient, shadow) are enabled.</summary>
    private static bool HasAnyEffects(FontGeneratorOptions options)
    {
        if (options.Outline > 0 || options.HasGradient || options.HasShadow)
            return true;

        if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is OutlinePostProcessor or GradientPostProcessor or ShadowPostProcessor)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Builds the list of layered effects from the generation options.</summary>
    private static List<IGlyphEffect> BuildEffects(FontGeneratorOptions options)
    {
        var effects = new List<IGlyphEffect>();

        // Detect shadow: from options properties or from a ShadowPostProcessor in the list.
        if (options.HasShadow)
        {
            effects.Add(new ShadowEffect(
                options.ShadowOffsetX, options.ShadowOffsetY, options.ShadowBlur,
                options.ShadowR, options.ShadowG, options.ShadowB, options.ShadowOpacity,
                options.HardShadow, options.ShadowBlurPasses, options.ShadowBlurKernelSize));
        }
        else if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is ShadowPostProcessor sp)
                {
                    effects.Add(new ShadowEffect(
                        sp.OffsetX, sp.OffsetY, sp.BlurRadius,
                        sp.ShadowR, sp.ShadowG, sp.ShadowB, sp.Opacity));
                    break;
                }
            }
        }

        // Detect outline: from options properties or from an OutlinePostProcessor in the list.
        // When a non-default channel layout is honored, the outline is routed into a channel
        // layer instead of being baked here. When that layout is skipped by the gate
        // (ShouldApplyChannelConfig false), the outline is baked into the composite as usual.
        if (options.Outline > 0 && !BmFont.ShouldApplyChannelConfig(options))
        {
            effects.Add(new OutlineEffect(options.Outline, options.OutlineR, options.OutlineG, options.OutlineB));
        }
        else if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is OutlinePostProcessor op && op.OutlineWidth > 0)
                {
                    effects.Add(new OutlineEffect(op.OutlineWidth, op.OutlineR, op.OutlineG, op.OutlineB));
                    break;
                }
            }
        }

        // Detect gradient: from options properties or from a GradientPostProcessor in the list.
        if (options.HasGradient)
        {
            effects.Add(new GradientEffect(
                options.GradientStartR!.Value, options.GradientStartG ?? 0, options.GradientStartB ?? 0,
                options.GradientEndR!.Value, options.GradientEndG ?? 0, options.GradientEndB ?? 0,
                options.GradientAngle, options.GradientMidpoint,
                options.GradientOffset, options.GradientScale, options.GradientCyclic));
        }
        else if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is GradientPostProcessor gp)
                {
                    effects.Add(new GradientEffect(
                        gp.StartR, gp.StartG, gp.StartB,
                        gp.EndR, gp.EndG, gp.EndB,
                        gp.AngleDegrees, gp.Midpoint));
                    break;
                }
            }
        }

        return effects;
    }

    /// <summary>Downscales a super-sampled glyph using a box filter.</summary>
    private static RasterizedGlyph SuperSampleDownscale(RasterizedGlyph glyph, int level)
    {
        if (glyph.Width == 0 || glyph.Height == 0)
            return glyph;

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var dstW = srcW / level;
        var dstH = srcH / level;

        if (dstW == 0) dstW = 1;
        if (dstH == 0) dstH = 1;

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dst = new byte[dstW * dstH * bpp];
        var srcPitch = glyph.Pitch;
        var area = level * level;

        for (var dy = 0; dy < dstH; dy++)
        {
            for (var dx = 0; dx < dstW; dx++)
            {
                if (bpp == 1)
                {
                    int sum = 0;
                    for (var sy = 0; sy < level; sy++)
                    {
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx);
                            if (srcIdx < glyph.BitmapData.Length)
                                sum += glyph.BitmapData[srcIdx];
                        }
                    }
                    dst[dy * dstW + dx] = (byte)(sum / area);
                }
                else
                {
                    // Use premultiplied alpha for correct edge blending.
                    // Without this, transparent pixels with gradient colors
                    // bleed dark halos into the edges during downscale.
                    float sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                    for (var sy = 0; sy < level; sy++)
                    {
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx) * 4;
                            if (srcIdx + 3 < glyph.BitmapData.Length)
                            {
                                var a = glyph.BitmapData[srcIdx + 3] / 255f;
                                sumR += glyph.BitmapData[srcIdx] * a;
                                sumG += glyph.BitmapData[srcIdx + 1] * a;
                                sumB += glyph.BitmapData[srcIdx + 2] * a;
                                sumA += a;
                            }
                        }
                    }
                    var dstIdx = (dy * dstW + dx) * 4;
                    if (sumA > 0)
                    {
                        dst[dstIdx] = (byte)Math.Min(255, sumR / sumA);
                        dst[dstIdx + 1] = (byte)Math.Min(255, sumG / sumA);
                        dst[dstIdx + 2] = (byte)Math.Min(255, sumB / sumA);
                    }
                    dst[dstIdx + 3] = (byte)Math.Min(255, sumA * 255 / area);
                }
            }
        }

        var metrics = glyph.Metrics;
        var newMetrics = new GlyphMetrics(
            BearingX: metrics.BearingX / level,
            BearingY: metrics.BearingY / level,
            Advance: metrics.Advance / level,
            Width: dstW,
            Height: dstH);

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW * bpp,
            Metrics = newMetrics,
            Format = glyph.Format
        };
    }

    /// <summary>Pads a glyph bitmap to the target height for uniform cell sizes.</summary>
    private static RasterizedGlyph EqualizeCellHeight(RasterizedGlyph glyph, int targetHeight)
    {
        if (glyph.Height >= targetHeight)
            return glyph;

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var newPitch = glyph.Width * bpp;
        var dst = new byte[newPitch * targetHeight];

        // Copy the original bitmap at the top (yoffset will handle alignment).
        for (var row = 0; row < glyph.Height; row++)
        {
            var srcOffset = row * glyph.Pitch;
            var dstOffset = row * newPitch;
            var rowBytes = Math.Min(glyph.Width * bpp, glyph.BitmapData.Length - srcOffset);
            if (rowBytes > 0)
                Array.Copy(glyph.BitmapData, srcOffset, dst, dstOffset, rowBytes);
        }

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = glyph.Width,
            Height = targetHeight,
            Pitch = newPitch,
            Metrics = glyph.Metrics,
            Format = glyph.Format
        };
    }

    /// <summary>Swaps in custom glyph images, replacing rasterized glyphs or adding new ones.</summary>
    private static List<RasterizedGlyph> ApplyCustomGlyphs(
        List<RasterizedGlyph> glyphs,
        Dictionary<int, CustomGlyph> customGlyphs,
        List<int> codepoints)
    {
        var result = new List<RasterizedGlyph>(glyphs.Count);
        var replaced = new HashSet<int>();

        foreach (var glyph in glyphs)
        {
            if (customGlyphs.TryGetValue(glyph.Codepoint, out var custom))
            {
                result.Add(CreateFromCustom(glyph.Codepoint, glyph.GlyphIndex, custom));
                replaced.Add(glyph.Codepoint);
            }
            else
            {
                result.Add(glyph);
            }
        }

        // Add custom glyphs for codepoints that weren't in the rasterized set.
        foreach (var (cp, custom) in customGlyphs)
        {
            if (!replaced.Contains(cp))
            {
                result.Add(CreateFromCustom(cp, 0, custom));
            }
        }

        return result;
    }

    private static RasterizedGlyph CreateFromCustom(int codepoint, int glyphIndex, CustomGlyph custom)
    {
        var bpp = custom.Format == PixelFormat.Rgba32 ? 4 : 1;
        var advance = custom.XAdvance ?? custom.Width;

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = custom.PixelData,
            Width = custom.Width,
            Height = custom.Height,
            Pitch = custom.Width * bpp,
            Metrics = new GlyphMetrics(
                BearingX: 0,
                BearingY: custom.Height,
                Advance: advance,
                Width: custom.Width,
                Height: custom.Height),
            Format = custom.Format
        };
    }
}
