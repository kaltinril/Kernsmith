using System.Buffers;
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
    private readonly VectorOutlineRequest? _vectorOutline;
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
        List<IGlyphPostProcessor>? activePostProcessors,
        VectorOutlineRequest? vectorOutline)
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
        _vectorOutline = vectorOutline;
    }

    /// <summary>
    /// Performs all per-font setup: validation, WOFF decompression, font parse, rasterizer
    /// creation/load, synthetic-style gating, variation axes, color palette, effective-ppem
    /// resolution, render-scale sizing, and effect/post-processor list construction.
    /// </summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF/WOFF2 file bytes.</param>
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

        if (options.WidthPercent is < 10 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(options), $"WidthPercent must be between 10 and 1000, was {options.WidthPercent}.");
        if (!float.IsFinite(options.ItalicAngle) || Math.Abs(options.ItalicAngle) > 60f)
            throw new ArgumentOutOfRangeException(nameof(options), $"ItalicAngle must be between -60 and 60 degrees, was {options.ItalicAngle}.");

        // 0b. Guard: SDF + super sampling is invalid — box filter corrupts distance values.
        var sdfMode = options.EffectiveSdfMode;
        var multiChannel = sdfMode is SdfMode.Msdf or SdfMode.Mtsdf;
        if (sdfMode != SdfMode.None && options.SuperSampleLevel > 1)
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

            // Guard: an MSDF glyph's RGB are three distances, not a color. Anything that packs,
            // tints, composites or re-routes channels would corrupt them.
            if (multiChannel)
                RejectMultiChannelIncompatibilities(options);

            // Guard: a vector outline is a coverage layer composited under the body, which means
            // nothing next to a distance field's encoded distances.
            if (options.OutlineMode == OutlineMode.Vector && options.Outline > 0 && sdfMode != SdfMode.None)
                throw new InvalidOperationException(
                    "Vector outlines (OutlineMode.Vector) cannot be combined with SDF rendering: the outline " +
                    "layer is coverage, and a distance field's values are distances. Use OutlineMode.Effect.");

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

            if (sdfMode == SdfMode.Sdf && !rasterizer.Capabilities.SupportsSdf)
            {
                throw new NotSupportedException(
                    $"Rasterizer backend does not support SDF rendering. " +
                    $"Use a backend that reports SupportsSdf = true (e.g., FreeType or StbTrueType).");
            }

            if (multiChannel && !rasterizer.Capabilities.SupportsMsdf)
            {
                throw new NotSupportedException(
                    "Rasterizer backend does not support MSDF/MTSDF rendering. " +
                    "Use a backend that reports SupportsMsdf = true (e.g., Native).");
            }

            // Synthetic width and slant are outline edits only some backends make; drawing a
            // different width or angle than asked for would be silently wrong output.
            if (options.WidthPercent != 100 && !rasterizer.Capabilities.SupportsWidthScale)
            {
                throw new NotSupportedException(
                    $"Rasterizer backend does not support synthetic width scaling (WidthPercent = {options.WidthPercent}). " +
                    "Use a backend that reports SupportsWidthScale = true (e.g., Native).");
            }

            if (effectiveItalic && !options.ItalicAngle.Equals(12f) && !rasterizer.Capabilities.SupportsItalicAngle)
            {
                throw new NotSupportedException(
                    $"Rasterizer backend does not support a synthetic italic angle other than 12 degrees (ItalicAngle = {options.ItalicAngle}). " +
                    "Use a backend that reports SupportsItalicAngle = true (e.g., Native).");
            }

            var rasterOptions = RasterOptions.FromGeneratorOptions(options) with
            {
                AntiAlias = AntiAliasNegotiation.Resolve(
                    options.AntiAlias, rasterizer.Capabilities.SupportedAntiAliasModes),
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

            // LCD output is never super sampled: box-filtering an N x render down averages each
            // channel across N subpixel strips, smearing the pattern the mode exists for. A
            // backend rendering LCD renders it at the final size (and is told SuperSample = 1, in
            // case it supersamples on its own); one that degraded LCD to grayscale supersamples
            // as usual. Distance fields win over LCD, and reject super sampling above.
            if (rasterOptions.AntiAlias == AntiAliasMode.Lcd && sdfMode == SdfMode.None)
            {
                ssLevel = 1;
                rasterOptions = rasterOptions with { SuperSample = 1 };
            }

            // SDF supersampling: render the distance field at a larger ppem, then box-average
            // back down. Unlike alpha-coverage super sampling (forbidden with SDF above
            // because averaging coverage corrupts distances), a FreeType SDF byte is locally
            // linear in signed distance (128 = edge crossing), so box-averaging preserves the
            // edge zero-crossing — this is the standard "render SDF high-res, then downsample"
            // technique. SDF and SuperSampleLevel>1 are mutually exclusive (rejected above), so
            // renderScale collapses to a single upscale factor for both paths.
            var sdfScale = sdfMode != SdfMode.None ? Math.Clamp(options.SdfScale, 1, 4) : 1;
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

            // Vector outlines: the backend draws the outline layer that the bitmap OutlineEffect (or,
            // for a routed outline, the channel path's OutlinePostProcessor) would otherwise draw.
            VectorOutlineRequest? vectorOutline = null;
            if (options.OutlineMode == OutlineMode.Vector && options.Outline > 0
                && rasterizer.Capabilities.SupportsOutlineStroke)
            {
                var routed = BmFont.ShouldApplyChannelConfig(options);
                if (!routed || BmFont.RoutesOutline(options.Channels!))
                {
                    vectorOutline = new VectorOutlineRequest(
                        // Same thickness definition as each mode's bitmap outline: the effect grows
                        // the glyph at the render (supersampled) size, the routed outline at the
                        // output size.
                        LayerWidth: routed ? options.Outline * renderScale : options.Outline,
                        options.OutlineShape,
                        HeightScale: Math.Max(10, options.HeightPercent) / 100f,
                        Routed: routed);
                }
            }

            return new PreparedRasterization(
                fontData, fontInfo, options, rasterizer, ownsRasterizer,
                rasterOptions, effectiveRasterOptions, effectiveSize, renderScale,
                effects, hasEffects, activePostProcessors, vectorOutline);
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

        // Vector outline layers are asked for here, one glyph at a time: rasterizers are not
        // thread-safe, and the per-glyph pass below runs in parallel.
        var vector = _vectorOutline;
        var masks = vector is not null ? RasterizeOutlineMasks(glyphs, vector) : null;

        // HeightStretch first (before custom glyphs, matching original order)
        if (options.HeightPercent != 100)
        {
            var stretch = new HeightStretchPostProcessor(options.HeightPercent);
            for (var i = 0; i < glyphs.Count; i++)
            {
                var unstretched = glyphs[i];
                glyphs[i] = stretch.Process(unstretched);
                if (masks is not null && masks.TryGetValue(unstretched.Codepoint, out var mask))
                    SetOrRemove(masks, unstretched.Codepoint,
                        mask.Stretch(unstretched.Height, glyphs[i].Height, threshold: options.AntiAlias == AntiAliasMode.None));
            }
        }

        // Custom glyphs (after stretch, before effects - matching original order)
        if (options.CustomGlyphs is { Count: > 0 })
        {
            glyphs = ApplyCustomGlyphs(glyphs, options.CustomGlyphs, codepointList);

            // A custom image has no outline for the backend to stroke: it keeps the bitmap outline.
            if (masks is not null)
            {
                foreach (var codepoint in options.CustomGlyphs.Keys)
                    masks.Remove(codepoint);
            }
        }

        // A routed outline is built from the final glyphs after this pass, so its layers follow
        // each glyph through compositing and downscaling. Arbitrary post-processors could move
        // the glyph in ways a layer cannot follow; with any active, glyphs keep the bitmap outline.
        var routedMasks = vector is { Routed: true } && masks is { Count: > 0 } && _activePostProcessors is null
            ? new OutlineMask?[glyphs.Count]
            : null;

        var needsDownscale = _renderScale > 1;

        // Multi-channel distance fields average each channel on its own: MTSDF alpha is a
        // distance, not opacity, so premultiplying RGB by it would destroy their distances.
        var perChannel = options.EffectiveSdfMode is SdfMode.Msdf or SdfMode.Mtsdf;

        // Atlas variants (e.g. a shadow silhouette) need the glyph's own bare coverage —
        // not a copy that may already have a baked outline/gradient/shadow composited into
        // its RGBA below — so snapshot the pre-effects glyph here (still applying the same
        // super-sample downscale as the primary, so scale matches).
        List<RasterizedGlyph>? rawGlyphsForVariants = options.Variants is { Count: > 0 }
            ? new List<RasterizedGlyph>(new RasterizedGlyph[glyphs.Count])
            : null;

        // Apply remaining per-glyph transforms in a single pass (parallelized on non-WASM,
        // above the small-batch threshold)
        if (_hasEffects || _activePostProcessors != null || needsDownscale || rawGlyphsForVariants != null || routedMasks != null)
        {
            void ProcessGlyph(int i)
            {
                var g = glyphs[i];
                OutlineMask? mask = null;
                masks?.TryGetValue(g.Codepoint, out mask);
                if (rawGlyphsForVariants != null)
                    rawGlyphsForVariants[i] = needsDownscale ? SuperSampleDownscale(g, _renderScale, perChannel) : g;
                if (_hasEffects)
                {
                    var effects = mask is not null && !vector!.Routed ? WithVectorOutline(mask) : _effects;
                    var unstyled = g;
                    g = GlyphCompositor.Composite(g, effects,
                        options.FillColorR, options.FillColorG, options.FillColorB, options.FillColorA);
                    mask = mask?.Shift(g.Metrics.BearingX - unstyled.Metrics.BearingX, unstyled.Metrics.BearingY - g.Metrics.BearingY);
                }
                if (_activePostProcessors != null)
                {
                    foreach (var processor in _activePostProcessors)
                        g = processor.Process(g);
                }
                if (needsDownscale)
                {
                    g = SuperSampleDownscale(g, _renderScale, perChannel);
                    if (routedMasks != null)
                        mask = mask?.Downscale(_renderScale);
                }
                if (routedMasks != null)
                    routedMasks[i] = mask;
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
            RawGlyphsForVariants = rawGlyphsForVariants,
            ChannelOutlineMasks = routedMasks is null ? null : CollectRoutedMasks(glyphs, routedMasks)
        };
    }

    /// <summary>
    /// How <see cref="OutlineMode.Vector"/> asks the backend for layers: the layer width in render
    /// pixels, the corner shape, the height stretch the layer will go through, and whether the
    /// outline is routed to a channel (built after the per-glyph pass) or composited as an effect.
    /// </summary>
    private sealed record VectorOutlineRequest(int LayerWidth, OutlineShape Shape, float HeightScale, bool Routed);

    /// <summary>
    /// Asks the backend for every non-blank glyph's outline layer, placed against the glyph. A
    /// glyph the backend returns no layer for is absent, and keeps the bitmap outline.
    /// </summary>
    private Dictionary<int, OutlineMask> RasterizeOutlineMasks(List<RasterizedGlyph> glyphs, VectorOutlineRequest vector)
    {
        var masks = new Dictionary<int, OutlineMask>();
        foreach (var glyph in glyphs)
        {
            // The bitmap outline skips blank glyphs too (GlyphCompositor returns them as they are).
            if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
                continue;

            var layer = _rasterizer.RasterizeOutlineLayer(
                glyph.Codepoint, _effectiveRasterOptions, vector.LayerWidth, vector.Shape, vector.HeightScale);
            if (layer is not null && OutlineMask.FromLayer(glyph, layer) is { } mask)
                masks[glyph.Codepoint] = mask;
        }

        return masks;
    }

    private static void SetOrRemove(Dictionary<int, OutlineMask> masks, int codepoint, OutlineMask? mask)
    {
        if (mask is null)
            masks.Remove(codepoint);
        else
            masks[codepoint] = mask;
    }

    /// <summary><see cref="_effects"/> with the bitmap outline replaced by the glyph's vector layer.</summary>
    private List<IGlyphEffect> WithVectorOutline(OutlineMask mask)
    {
        var options = Options;
        var effects = new List<IGlyphEffect>(_effects.Count);
        foreach (var effect in _effects)
        {
            effects.Add(effect is OutlineEffect
                ? new VectorOutlineEffect(mask, options.Outline, options.OutlineR, options.OutlineG, options.OutlineB)
                : effect);
        }

        return effects;
    }

    private static Dictionary<int, OutlineMask> CollectRoutedMasks(List<RasterizedGlyph> glyphs, OutlineMask?[] masks)
    {
        var byCodepoint = new Dictionary<int, OutlineMask>();
        for (var i = 0; i < masks.Length; i++)
        {
            if (masks[i] is { } mask)
                byCodepoint[glyphs[i].Codepoint] = mask;
        }

        return byCodepoint;
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

    /// <summary>
    /// Throws for options that treat a glyph's RGB as color, which an MSDF/MTSDF glyph's are not.
    /// </summary>
    private static void RejectMultiChannelIncompatibilities(FontGeneratorOptions options)
    {
        string? conflict = null;
        if (options.ChannelPacking)
            conflict = "channel packing";
        else if (HasAnyEffects(options))
            conflict = "effects (outline, gradient, shadow)";
        else if (options.FillColorR != 255 || options.FillColorG != 255 || options.FillColorB != 255 || options.FillColorA != 255)
            conflict = "a fill color";
        else if (BmFont.ShouldApplyChannelConfig(options))
            conflict = "a custom channel configuration";
        else if (options.Variants is { Count: > 0 })
            conflict = "atlas variants";
        else if (options.ColorFont)
            conflict = "color font rendering";

        if (conflict is not null)
            throw new InvalidOperationException(
                $"MSDF/MTSDF rendering cannot be combined with {conflict}: the RGB channels hold " +
                "distances, not color.");
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
        // A shadow routed into its own channel is drawn there, from these glyphs (BmFont's
        // channel shadow glyphs); baking it here too would put it in the Glyph channels and
        // shadow the shadow.
        if (options.HasShadow)
        {
            if (!BmFont.RoutesShadow(options))
            {
                effects.Add(new ShadowEffect(
                    options.ShadowOffsetX, options.ShadowOffsetY, options.ShadowBlur,
                    options.ShadowR, options.ShadowG, options.ShadowB, options.ShadowOpacity,
                    options.HardShadow, options.ShadowBlurPasses, options.ShadowBlurKernelSize));
            }
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
            effects.Add(new OutlineEffect(options.Outline, options.OutlineR, options.OutlineG, options.OutlineB,
                smooth: options.AntiAlias != AntiAliasMode.None, shape: options.OutlineShape));
        }
        else if (options.PostProcessors != null)
        {
            foreach (var pp in options.PostProcessors)
            {
                if (pp is OutlinePostProcessor op && op.OutlineWidth > 0)
                {
                    effects.Add(new OutlineEffect(op.OutlineWidth, op.OutlineR, op.OutlineG, op.OutlineB,
                        smooth: op.Smooth && options.AntiAlias != AntiAliasMode.None, shape: op.Shape));
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

    /// <summary>
    /// Downscales a super-sampled glyph using a box filter. RGBA is averaged premultiplied by
    /// alpha (color glyphs), or with every channel on its own when <paramref name="perChannel"/>
    /// is set (multi-channel distance fields), exactly as grayscale is.
    /// </summary>
    internal static RasterizedGlyph SuperSampleDownscale(RasterizedGlyph glyph, int level, bool perChannel = false)
    {
        // A blank glyph (space) has no pixels, but its metrics were measured at the
        // super-sampled size like every other glyph's.
        if (glyph.Width == 0 || glyph.Height == 0)
        {
            var m = glyph.Metrics;
            return new RasterizedGlyph
            {
                Codepoint = glyph.Codepoint,
                GlyphIndex = glyph.GlyphIndex,
                BitmapData = glyph.BitmapData,
                Width = glyph.Width,
                Height = glyph.Height,
                Pitch = glyph.Pitch,
                Metrics = m with
                {
                    BearingX = m.BearingX / level,
                    BearingY = m.BearingY / level,
                    Advance = m.Advance / level,
                },
                Format = glyph.Format,
            };
        }

        var srcW = glyph.Width;
        var srcH = glyph.Height;
        var dstW = srcW / level;
        var dstH = srcH / level;

        if (dstW == 0) dstW = 1;
        if (dstH == 0) dstH = 1;

        var bpp = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        var dst = new byte[dstW * dstH * bpp];

        if (bpp == 1 || perChannel)
            BoxSum(glyph.BitmapData, glyph.Pitch, level, dstW, dstH, bpp, dst);
        else
            BoxPremultiplied(glyph.BitmapData, glyph.Pitch, level, dstW, dstH, dst);

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

    /// <summary>
    /// Each channel of each destination pixel is the integer mean (truncated) of its
    /// <paramref name="level"/> x <paramref name="level"/> source block, a byte past the end of
    /// <paramref name="src"/> counting as 0 (multi-channel distance fields average every channel
    /// on its own; grayscale has one). Source rows are swept in order into per-pixel sums.
    /// </summary>
    private static void BoxSum(byte[] src, int srcPitch, int level, int dstW, int dstH, int bpp, byte[] dst)
    {
        var area = level * level;
        var rowBytes = dstW * level * bpp;
        var sums = ArrayPool<int>.Shared.Rent(dstW * bpp);
        try
        {
            for (var dy = 0; dy < dstH; dy++)
            {
                var acc = sums.AsSpan(0, dstW * bpp);
                acc.Clear();
                for (var sy = 0; sy < level; sy++)
                {
                    var rowStart = (dy * level + sy) * srcPitch;
                    if (rowStart + rowBytes <= src.Length)
                    {
                        var row = src.AsSpan(rowStart, rowBytes);
                        var i = 0;
                        for (var dx = 0; dx < dstW; dx++)
                        {
                            var k = dx * bpp;
                            for (var sx = 0; sx < level; sx++)
                            {
                                for (var c = 0; c < bpp; c++)
                                    acc[k + c] += row[i++];
                            }
                        }
                    }
                    else
                    {
                        var i = rowStart;
                        for (var dx = 0; dx < dstW; dx++)
                        {
                            var k = dx * bpp;
                            for (var sx = 0; sx < level; sx++)
                            {
                                for (var c = 0; c < bpp; c++, i++)
                                {
                                    if (i < src.Length)
                                        acc[k + c] += src[i];
                                }
                            }
                        }
                    }
                }

                var output = dst.AsSpan(dy * dstW * bpp, dstW * bpp);
                for (var k = 0; k < output.Length; k++)
                    output[k] = (byte)(acc[k] / area);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(sums);
        }
    }

    /// <summary>
    /// Premultiplied-alpha box filter for RGBA: without premultiplying, transparent pixels with
    /// gradient colors bleed dark halos into the edges during downscale. Each destination
    /// pixel's float sums take their source pixels in block order (row by row, left to right)
    /// — the order that fixes their rounding — and a pixel whose bytes run past the end of
    /// <paramref name="src"/> is skipped.
    /// </summary>
    private static void BoxPremultiplied(byte[] src, int srcPitch, int level, int dstW, int dstH, byte[] dst)
    {
        var area = level * level;
        var sums = ArrayPool<float>.Shared.Rent(dstW * 4);
        try
        {
            for (var dy = 0; dy < dstH; dy++)
            {
                var acc = sums.AsSpan(0, dstW * 4);
                acc.Clear();
                for (var sy = 0; sy < level; sy++)
                {
                    var rowStart = (dy * level + sy) * srcPitch;
                    for (var dx = 0; dx < dstW; dx++)
                    {
                        var k = dx * 4;
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = rowStart + (dx * level + sx) * 4;
                            if (srcIdx + 3 < src.Length)
                            {
                                var a = src[srcIdx + 3] / 255f;
                                acc[k] += src[srcIdx] * a;
                                acc[k + 1] += src[srcIdx + 1] * a;
                                acc[k + 2] += src[srcIdx + 2] * a;
                                acc[k + 3] += a;
                            }
                        }
                    }
                }

                for (var dx = 0; dx < dstW; dx++)
                {
                    var k = dx * 4;
                    float sumR = acc[k], sumG = acc[k + 1], sumB = acc[k + 2], sumA = acc[k + 3];
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
        finally
        {
            ArrayPool<float>.Shared.Return(sums);
        }
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
