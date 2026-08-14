using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// The main API for generating and loading BMFont bitmap fonts.
/// </summary>
public static class BmFont
{
    private static readonly Lazy<DefaultSystemFontProvider> s_systemFontProvider = new();

    /// <summary>Shared system font provider, used by FontCache.</summary>
    internal static DefaultSystemFontProvider SystemFontProvider => s_systemFontProvider.Value;

    // Key: "FamilyName" or "FamilyName|Style" (lowercase)
    private static readonly ConcurrentDictionary<string, FontLoadResult> s_fontRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers raw font data (TTF/OTF/WOFF) under a family name so that
    /// <see cref="GenerateFromSystem(string, FontGeneratorOptions?)"/> can resolve it without accessing system fonts.
    /// This is essential on platforms where system fonts are unavailable (e.g., Blazor WASM)
    /// and recommended for cross-platform consistency.
    /// </summary>
    /// <param name="familyName">Font family name (e.g., "Arial").</param>
    /// <param name="fontData">Raw font file bytes.</param>
    /// <param name="style">
    /// Optional style name (e.g., "Bold", "Italic", "Bold Italic").
    /// When null, registers as the default/regular variant.
    /// </param>
    /// <param name="faceIndex">TTC face index (0 for single-face font files).</param>
    public static void RegisterFont(string familyName, byte[] fontData, string? style = null, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        ArgumentNullException.ThrowIfNull(fontData);

        var key = style == null ? familyName : $"{familyName}|{style}";
        s_fontRegistry[key] = new FontLoadResult(fontData, faceIndex);
    }

    /// <summary>
    /// Pre-populates the system font resolver's cache with a consumer-supplied file path for
    /// a family name, so <see cref="GenerateFromSystem(string, FontGeneratorOptions?)"/> can
    /// skip OS-specific resolution (Windows registry, heuristic filename match, full directory
    /// scan) for it entirely. This is the lightweight alternative to <see cref="RegisterFont"/>
    /// for a consumer who already knows where one of its fonts lives on disk but doesn't want
    /// to load its bytes up front.
    /// </summary>
    /// <remarks>
    /// The hint is validated exactly like any other cache or seed-table entry — the file must
    /// exist and its parsed font-table family name must match <paramref name="familyName"/> —
    /// before it is ever trusted. A wrong hint costs one bounded failed check, then normal
    /// resolution proceeds as if no hint had been given; a correct hint skips OS resolution
    /// entirely on the next lookup for that family.
    /// </remarks>
    /// <param name="familyName">Font family name the hint applies to (e.g., "Arial").</param>
    /// <param name="path">Path to the font file believed to contain that family.</param>
    /// <param name="faceIndex">TTC face index (0 for single-face font files).</param>
    public static void HintFontLocation(string familyName, string path, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        ArgumentNullException.ThrowIfNull(path);

        s_systemFontProvider.Value.AddResolvedFontHint(familyName, path, faceIndex);
    }

    /// <summary>
    /// Removes a previously registered font. Returns true if a font was removed.
    /// </summary>
    /// <param name="familyName">Font family name.</param>
    /// <param name="style">Optional style name, or null for the default variant.</param>
    public static bool UnregisterFont(string familyName, string? style = null)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        var key = style == null ? familyName : $"{familyName}|{style}";
        return s_fontRegistry.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all registered fonts.
    /// </summary>
    public static void ClearRegisteredFonts() => s_fontRegistry.Clear();

    /// <summary>
    /// Attempts to load a font from the registry.
    /// </summary>
    private static FontLoadResult? LoadFromRegistry(string familyName, string? styleName = null)
    {
        if (styleName != null && s_fontRegistry.TryGetValue($"{familyName}|{styleName}", out var styled))
            return styled;

        if (s_fontRegistry.TryGetValue(familyName, out var regular))
            return regular;

        return null;
    }

    /// <summary>Generates a BMFont from raw font bytes.</summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult Generate(byte[] fontData, FontGeneratorOptions? options = null)
        => GenerateCore(fontData, options, sourceFontFile: null, sourceFontName: null);

    #region Incremental sessions (phase 183 — runtime glyph addition with stable packing)

    /// <summary>
    /// Starts an empty <see cref="BmFontIncrementalSession"/> for adding glyphs at runtime.
    /// The font is parsed once without a character-set filter, so any character the font
    /// contains can be added later. The first
    /// <see cref="BmFontIncrementalSession.AddGlyphs(string)"/> call computes the initial
    /// atlas page size exactly as <see cref="Generate(byte[], FontGeneratorOptions?)"/> would for that batch.
    /// </summary>
    /// <remarks>
    /// Not supported in a session (throws <see cref="NotSupportedException"/>):
    /// <see cref="FontGeneratorOptions.Variants"/>, <see cref="FontGeneratorOptions.ChannelPacking"/>,
    /// <see cref="FontGeneratorOptions.TargetRegion"/>, <see cref="FontGeneratorOptions.CustomGlyphs"/>.
    /// A <see cref="PackingAlgorithm.Skyline"/> configuration — or a custom
    /// <see cref="FontGeneratorOptions.Packer"/> — silently uses MaxRects for
    /// additions (arbitrary packer state cannot be reconstructed incrementally).
    /// </remarks>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <param name="overflowPolicy">What to do when a new glyph does not fit (default: grow the page).</param>
    public static BmFontIncrementalSession BeginIncremental(
        byte[] fontData,
        FontGeneratorOptions? options = null,
        AdditionOverflowPolicy overflowPolicy = AdditionOverflowPolicy.Grow)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        options ??= new FontGeneratorOptions();
        ValidateIncrementalOptions(options);
        return BmFontIncrementalSession.Begin(fontData, options, overflowPolicy);
    }

    /// <summary>
    /// Resumes a <see cref="BmFontIncrementalSession"/> from an existing model (e.g. a
    /// parsed .fnt or a previous <see cref="Generate(byte[], FontGeneratorOptions?)"/> result): occupancy, page geometry,
    /// character list and kerning are recovered from <paramref name="existing"/>, and new
    /// glyphs are placed without moving anything already there. <paramref name="options"/>
    /// must be the same settings the model was generated with — padding, spacing, size and
    /// outline mismatches throw <see cref="ArgumentException"/>. See
    /// <see cref="BeginIncremental"/> for the options unsupported in a session.
    /// </summary>
    /// <remarks>
    /// <see cref="FontGeneratorOptions.Bold"/>, <see cref="FontGeneratorOptions.Italic"/> and
    /// <see cref="FontGeneratorOptions.MatchCharHeight"/> cannot be verified from the model
    /// and must match the original generation, or added glyphs will not match the atlas.
    /// </remarks>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes of the same font the model was generated from.</param>
    /// <param name="options">The generation options the existing model was generated with.</param>
    /// <param name="existing">The existing font model providing occupancy and metrics.</param>
    /// <param name="overflowPolicy">What to do when a new glyph does not fit (default: grow the page).</param>
    public static BmFontIncrementalSession ResumeIncremental(
        byte[] fontData,
        FontGeneratorOptions options,
        BmFontModel existing,
        AdditionOverflowPolicy overflowPolicy = AdditionOverflowPolicy.Grow)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(existing);
        ValidateIncrementalOptions(options);
        return BmFontIncrementalSession.Resume(fontData, options, existing, overflowPolicy);
    }

    /// <summary>v1 unsupported-options guard for incremental sessions (phase 183).</summary>
    private static void ValidateIncrementalOptions(FontGeneratorOptions options)
    {
        if (options.MaxTextureWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureWidth must be positive, was {options.MaxTextureWidth}.");
        if (options.MaxTextureHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureHeight must be positive, was {options.MaxTextureHeight}.");
        if (options.Variants is { Count: > 0 })
            throw new NotSupportedException("FontGeneratorOptions.Variants is not supported in an incremental session.");
        if (options.ChannelPacking)
            throw new NotSupportedException("FontGeneratorOptions.ChannelPacking is not supported in an incremental session.");
        if (options.TargetRegion is not null)
            throw new NotSupportedException("FontGeneratorOptions.TargetRegion is not supported in an incremental session.");
        if (options.CustomGlyphs is { Count: > 0 })
            throw new NotSupportedException("FontGeneratorOptions.CustomGlyphs is not supported in an incremental session.");
    }

    #endregion

    internal static RasterizationResult RasterizeFont(byte[] fontData, FontGeneratorOptions options, string? systemFontName = null)
    {
        // One-shot composition of the prepare/per-add split (phase 183): setup once, rasterize
        // the resolved character set once, then release the setup (PreparedRasterization honors
        // the caller-owned options.Rasterizer rule on Dispose).
        using var prepared = PreparedRasterization.Prepare(
            fontData, options, systemFontName, subsetToCharacterSet: true);
        var codepoints = prepared.ResolveCodepoints();
        return prepared.RasterizeAndProcess(codepoints, equalizeTargetHeight: null);
    }

    internal static int EncodeCombinedId(int fontIndex, int codepoint)
    {
        if ((uint)fontIndex > 0x7FF)
            throw new ArgumentOutOfRangeException(nameof(fontIndex), $"Font index {fontIndex} exceeds the maximum of 2047 for combined ID encoding.");
        return (fontIndex << 21) | (codepoint & 0x1FFFFF);
    }
    internal static (int FontIndex, int Codepoint) DecodeCombinedId(int combinedId) => (combinedId >>> 21, combinedId & 0x1FFFFF);

    private static BmFontResult GenerateCore(byte[] fontData, FontGeneratorOptions? options, string? sourceFontFile, string? sourceFontName, string? systemFontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        options ??= new FontGeneratorOptions();

        if (options.MaxTextureWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureWidth must be positive, was {options.MaxTextureWidth}.");
        if (options.MaxTextureHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureHeight must be positive, was {options.MaxTextureHeight}.");
        if (options.Variants is { Count: > 0 } && options.TargetRegion is not null)
            throw new NotSupportedException("FontGeneratorOptions.Variants is not supported together with TargetRegion.");
        if (options.Variants is { Count: > 0 } && options.ChannelPacking)
            throw new NotSupportedException("FontGeneratorOptions.Variants is not supported together with ChannelPacking.");
        if (options.Variants is { Count: > 0 } && ShouldApplyChannelConfig(options))
            throw new NotSupportedException("FontGeneratorOptions.Variants is not supported together with a custom Channels configuration.");

        var metrics = options.CollectMetrics ? new PipelineMetrics() : null;

        metrics?.Begin("Rasterization");
        var rasterResult = RasterizeFont(fontData, options, systemFontFamily);
        metrics?.End();

        var fontInfo = rasterResult.FontInfo;
        var glyphs = rasterResult.Glyphs;
        var failedCodepoints = rasterResult.FailedCodepoints;

        // 5. Pack into atlas (Phase 78F outline empty-glyph substitution already ran
        // inside RasterizeAndProcess)
        var packer = options.Packer ?? (options.PackingAlgorithm == PackingAlgorithm.Skyline
            ? new SkylinePacker()
            : new MaxRectsPacker());
        var padding = options.Padding;
        var spacing = options.Spacing;

        // Issue #115: a custom ChannelConfig that routes outline content into a channel
        // produces glyphs that are larger than the base glyphs (each dimension grows by
        // 2*outlineWidth). Generate those outline glyphs up front so the atlas size estimate,
        // the AutofitTexture verification pack, and the real pack all operate on the SAME
        // (expanded) dimensions. Otherwise sizing is computed from the smaller base glyphs and
        // the larger outline glyphs spill onto extra pages — while the default-channel path,
        // which expands glyphs during rasterization, sizes correctly.
        IReadOnlyList<RasterizedGlyph>? channelOutlineGlyphs = null;
        // Issue #167: a Shadow-content channel needs the shadow's own expanded coverage
        // (no glyph composited on top) so it can be recolored independently at draw time.
        // Generated up front for the same sizing reason as outline above.
        IReadOnlyList<RasterizedGlyph>? channelShadowGlyphs = null;
        if (ShouldApplyChannelConfig(options) && options.Channels is { } cfg)
        {
            var needsOutline = cfg.Alpha is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                || cfg.Red is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                || cfg.Green is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                || cfg.Blue is ChannelContent.Outline or ChannelContent.GlyphAndOutline;
            // Only synthesize an outline layer when a real outline width is configured.
            // With outlineThickness=0 a forced 1px outline would grow every glyph by ~1px;
            // instead the Outline-content channels fall back to glyph coverage in the compositor.
            if (needsOutline && options.Outline > 0)
            {
                var outlineProcessor = new OutlinePostProcessor(options.Outline, options.OutlineR, options.OutlineG, options.OutlineB);
                channelOutlineGlyphs = glyphs.Select(g => outlineProcessor.Process(g)).ToList();
            }

            var needsShadow = cfg.Alpha == ChannelContent.Shadow
                || cfg.Red == ChannelContent.Shadow
                || cfg.Green == ChannelContent.Shadow
                || cfg.Blue == ChannelContent.Shadow;
            if (needsShadow && options.HasShadow)
            {
                var shadowProcessor = new ShadowCoveragePostProcessor(
                    options.ShadowOffsetX, options.ShadowOffsetY, options.ShadowBlur,
                    options.ShadowOpacity, options.HardShadow, options.ShadowBlurPasses, options.ShadowBlurKernelSize);
                channelShadowGlyphs = glyphs.Select(g => shadowProcessor.Process(g)).ToList();
            }
        }

        // Use the larger of the outline/shadow-expanded glyphs (when present) for atlas
        // sizing/packing so the packed rectangles match the glyphs that actually get
        // composited. When both outline and shadow channels are configured together, size
        // against whichever expands each glyph further so neither overlay is clipped.
        var rectGlyphs = (channelOutlineGlyphs, channelShadowGlyphs) switch
        {
            (not null, not null) => channelOutlineGlyphs
                .Zip(channelShadowGlyphs, (o, s) => o.Width * o.Height >= s.Width * s.Height ? o : s)
                .ToList(),
            (not null, null) => channelOutlineGlyphs,
            (null, not null) => channelShadowGlyphs,
            _ => glyphs
        };
        var glyphRects = rectGlyphs.Select(g => new GlyphRect(
            g.Codepoint,
            g.Width + padding.Left + padding.Right + spacing.Horizontal,
            g.Height + padding.Up + padding.Down + spacing.Vertical
        )).ToList();

        int pageWidth, pageHeight;

        // Target region: render into an existing PNG image.
        if (options.TargetRegion is { } region)
        {
            // Load source PNG bytes.
            byte[] sourcePngBytes;
            if (region.SourcePngData != null)
            {
                sourcePngBytes = region.SourcePngData;
            }
            else if (region.SourcePngPath != null)
            {
                if (!File.Exists(region.SourcePngPath))
                    throw new FileNotFoundException($"Source PNG file not found: {region.SourcePngPath}", region.SourcePngPath);
                sourcePngBytes = File.ReadAllBytes(region.SourcePngPath);
            }
            else
            {
                throw new ArgumentException("TargetRegion must specify either SourcePngPath or SourcePngData.");
            }

            // Validate region dimensions.
            if (region.Width <= 0 || region.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "TargetRegion Width and Height must be positive.");

            // Decode source PNG.
            var (sourcePixels, sourceWidth, sourceHeight) = StbPngDecoder.DecodePng(sourcePngBytes);

            // Validate region fits within source image.
            if (region.X < 0 || region.Y < 0
                || region.X + region.Width > sourceWidth
                || region.Y + region.Height > sourceHeight)
                throw new ArgumentException(
                    $"TargetRegion ({region.X},{region.Y} {region.Width}x{region.Height}) exceeds source image bounds ({sourceWidth}x{sourceHeight}).");

            // Pack glyphs into the region dimensions.
            pageWidth = region.Width;
            pageHeight = region.Height;

            metrics?.Begin("AtlasPacking");
            var packResult = packer.Pack(glyphRects, pageWidth, pageHeight);
            metrics?.End();

            if (packResult.PageCount > 1)
                throw new AtlasPackingException("Glyphs do not fit in target region.");

            // Build atlas page at region dimensions.
            metrics?.Begin("AtlasEncoding");
            var encoder = options.AtlasEncoder ?? new StbPngEncoder();
            var atlasPages = AtlasBuilder.Build(glyphs, packResult, padding, encoder);
            var atlasPage = atlasPages[0];

            // Ensure atlas page is RGBA for compositing.
            var atlasPixels = atlasPage.PixelData;
            if (atlasPage.Format == PixelFormat.Grayscale8)
            {
                // Promote grayscale to RGBA (white with alpha).
                var rgba = new byte[atlasPage.Width * atlasPage.Height * 4];
                for (var i = 0; i < atlasPage.Width * atlasPage.Height; i++)
                {
                    rgba[i * 4 + 0] = 255;
                    rgba[i * 4 + 1] = 255;
                    rgba[i * 4 + 2] = 255;
                    rgba[i * 4 + 3] = atlasPixels[i];
                }
                atlasPixels = rgba;
            }

            // Composite atlas page onto source image.
            AtlasBuilder.CompositeOnto(sourcePixels, sourceWidth, region.X, region.Y, atlasPixels, atlasPage.Width, atlasPage.Height);

            // Re-encode the composited full image as the output page.
            var compositedPage = new AtlasPage
            {
                PageIndex = 0,
                Width = sourceWidth,
                Height = sourceHeight,
                PixelData = sourcePixels,
                Format = PixelFormat.Rgba32
            };
            compositedPage.SetEncoder(encoder);
            var pages = new List<AtlasPage> { compositedPage };
            metrics?.End();

            // Build BmFontModel with offsets so char coordinates are in full-image space.
            metrics?.Begin("ModelAssembly");
            var model = BmFontModelBuilder.Build(fontInfo, glyphs, packResult, options,
                charOffsetX: region.X, charOffsetY: region.Y,
                overrideScaleW: sourceWidth, overrideScaleH: sourceHeight,
                effectiveSize: rasterResult.EffectiveSize,
                rasterizerFontMetrics: rasterResult.RasterizerFontMetrics,
                rasterizerKerningPairs: rasterResult.RasterizerKerningPairs);
            metrics?.End();

            return new BmFontResult(model, pages, failedCodepoints, metrics, options, sourceFontFile, sourceFontName);
        }

        // Build sizing options from generator options.
        var sizingOptions = AtlasSizeEstimator.BuildSizingOptions(options);

        // Atlas variants (phase-182 / issue #175): additional character-set renderings (e.g. a
        // dropshadow silhouette), packed into the SAME shared atlas as the primary glyphs via
        // AtlasGroupBuilder, each still producing its own complete BmFontModel/.fnt.
        if (options.Variants is { Count: > 0 })
        {
            return GenerateWithVariants(fontInfo, glyphs, glyphRects, options, packer, padding, spacing,
                sizingOptions, rasterResult, failedCodepoints, metrics, sourceFontFile, sourceFontName);
        }

        metrics?.Begin("AtlasSizeEstimation");
        // Estimate + constraints + (with AutofitTexture) a one-page verification pack
        // and one-step bump — the same sequence a session's first add runs.
        (pageWidth, pageHeight) = AtlasSizeEstimator.ComputeInitialPageSize(
            glyphRects, options, sizingOptions,
            (w, h) => packer.Pack(glyphRects, w, h).PageCount == 1);
        metrics?.End();

        metrics?.Begin("AtlasPacking");
        {
            var packResult = packer.Pack(glyphRects, pageWidth, pageHeight);
            metrics?.End();

            // 6. Build atlas pages
            metrics?.Begin("AtlasEncoding");
            var encoder = options.AtlasEncoder ?? (options.TextureFormat switch
            {
                TextureFormat.Tga => new TgaEncoder(),
                TextureFormat.Dds => new DdsEncoder(),
                _ => new StbPngEncoder()
            });
            IReadOnlyList<AtlasPage> pages;
            IReadOnlyDictionary<int, int>? glyphChannels = null;

            if (ShouldApplyChannelConfig(options) && options.Channels is { } channelConfig)
            {
                // Outline/shadow glyphs (when any channel needs them) were generated up front
                // and already drove the atlas size estimate, the AutofitTexture verification,
                // and packResult above — so the packed cells are sized for the largest fringe
                // and the result stays on a single page when it fits (issue #115, issue #167).
                var outlineGlyphs = channelOutlineGlyphs;
                var shadowGlyphs = channelShadowGlyphs;

                // Build atlas pages with original glyphs for glyph channels, outline glyphs
                // for outline channels, and shadow glyphs for the shadow-only channel.
                pages = ChannelCompositor.Build(glyphs, outlineGlyphs, shadowGlyphs, packResult, padding, channelConfig, encoder);

                if (outlineGlyphs != null || shadowGlyphs != null)
                {
                    // Use the per-glyph larger of outline/shadow (rectGlyphs, computed above)
                    // for the model so .fnt metrics (width, height, xoffset, yoffset, xadvance)
                    // reflect whichever expansion the packed cell was actually sized for —
                    // otherwise the unused overlay's pixels would be cropped by the quad.
                    glyphs = rectGlyphs.ToList();
                }
            }
            else if (options.ChannelPacking)
            {
                var channelResult = ChannelPackedAtlasBuilder.Build(glyphs, packResult, padding, encoder);
                pages = channelResult.Pages;
                glyphChannels = channelResult.GlyphChannels;
            }
            else
            {
                pages = AtlasBuilder.Build(glyphs, packResult, padding, encoder);
            }
            metrics?.End();

            // 7. Assemble BMFont model
            metrics?.Begin("ModelAssembly");
            var model = BmFontModelBuilder.Build(fontInfo, glyphs, packResult, options, glyphChannels,
                effectiveSize: rasterResult.EffectiveSize,
                rasterizerFontMetrics: rasterResult.RasterizerFontMetrics,
                rasterizerKerningPairs: rasterResult.RasterizerKerningPairs);
            metrics?.End();

            return new BmFontResult(model, pages, failedCodepoints, metrics, options, sourceFontFile, sourceFontName);
        }
    }

    /// <summary>
    /// Generates a <see cref="BmFontResult"/> when <see cref="FontGeneratorOptions.Variants"/> is
    /// non-empty: primary glyphs and each variant's glyphs (e.g. a dropshadow silhouette) are
    /// packed together into ONE shared atlas via <see cref="AtlasGroupBuilder"/>, so the primary
    /// and every variant <see cref="BmFontModel"/> reference the exact same <see cref="PageEntry"/>
    /// filenames — one physical PNG serves all of them. See phase-182 (issue #175).
    /// </summary>
    private static BmFontResult GenerateWithVariants(
        FontInfo fontInfo,
        IReadOnlyList<RasterizedGlyph> glyphs,
        IReadOnlyList<GlyphRect> primaryRects,
        FontGeneratorOptions options,
        IAtlasPacker packer,
        Padding padding,
        Spacing spacing,
        AtlasSizingOptions sizingOptions,
        RasterizationResult rasterResult,
        IReadOnlyList<int> failedCodepoints,
        PipelineMetrics? metrics,
        string? sourceFontFile,
        string? sourceFontName)
    {
        metrics?.Begin("VariantGeneration");

        // Guaranteed non-null: RasterizeFont populates this whenever options.Variants is non-empty.
        var variantSourceGlyphs = rasterResult.RawGlyphsForVariants!;

        var variantGlyphsByName = new Dictionary<string, List<RasterizedGlyph>>();
        var groupSources = new List<AtlasGroupBuilder.GroupSource> { new("primary", primaryRects) };

        foreach (var variant in options.Variants!)
        {
            IGlyphPostProcessor variantProcessor = variant.Kind switch
            {
                AtlasVariantKind.ShadowSilhouette => new ShadowCoveragePostProcessor(
                    offsetX: 0, offsetY: 0, blurRadius: variant.BlurRadius,
                    opacity: 1.0f, hardShadow: variant.HardShadow),
                _ => throw new NotSupportedException($"Unsupported AtlasVariantKind: {variant.Kind}")
            };

            var variantGlyphs = variantSourceGlyphs.Select(variantProcessor.Process).ToList();
            var variantRects = variantGlyphs.Select(g => new GlyphRect(
                g.Codepoint,
                g.Width + padding.Left + padding.Right + spacing.Horizontal,
                g.Height + padding.Up + padding.Down + spacing.Vertical
            )).ToList();

            variantGlyphsByName[variant.Name] = variantGlyphs;
            groupSources.Add(new AtlasGroupBuilder.GroupSource(variant.Name, variantRects));
        }

        // Combined sizing across every source's rects (phase-182 item 2), not just the primary's.
        var combinedRects = groupSources.SelectMany(s => s.Rects).ToList();
        var groupSizingOptions = sizingOptions with { ChannelPacking = false, EqualizedCellHeights = false };

        metrics?.Begin("AtlasSizeEstimation");
        var (groupWidth, groupHeight) = AtlasSizeEstimator.Estimate(combinedRects, groupSizingOptions);
        metrics?.End();

        if (options.SizeConstraints is { } sizeConstraints)
        {
            var (cw, ch) = AtlasSizeEstimator.ApplyConstraints(
                groupWidth, groupHeight, sizeConstraints, groupSizingOptions, combinedRects);
            groupWidth = cw;
            groupHeight = ch;
        }

        metrics?.Begin("AtlasPacking");
        var (shared, placementsByTag) = AtlasGroupBuilder.Pack(packer, groupSources, groupWidth, groupHeight);

        if (options.AutofitTexture && shared.PageCount > 1)
        {
            // One-step bump: double the smaller dimension (or next POT), then repack once.
            if (groupWidth <= groupHeight)
                groupWidth = groupSizingOptions.PowerOfTwo ? groupWidth * 2 : (int)(groupWidth * 1.5);
            else
                groupHeight = groupSizingOptions.PowerOfTwo ? groupHeight * 2 : (int)(groupHeight * 1.5);

            groupWidth = Math.Min(groupWidth, options.MaxTextureWidth);
            groupHeight = Math.Min(groupHeight, options.MaxTextureHeight);

            (shared, placementsByTag) = AtlasGroupBuilder.Pack(packer, groupSources, groupWidth, groupHeight);
        }
        metrics?.End();

        // Render ALL sources' glyphs onto one shared set of atlas pages. AtlasBuilder.Build
        // keys glyphs by Codepoint against packResult.Placements' Id — since composite ids
        // (not original codepoints) are what shared.Placements carries, re-tag each render
        // glyph's Codepoint with the same composite id AtlasGroupBuilder computed internally.
        metrics?.Begin("AtlasEncoding");
        var encoder = options.AtlasEncoder ?? (options.TextureFormat switch
        {
            TextureFormat.Tga => new TgaEncoder(),
            TextureFormat.Dds => new DdsEncoder(),
            _ => new StbPngEncoder()
        });

        var renderGlyphs = new List<RasterizedGlyph>();
        for (var sourceIndex = 0; sourceIndex < groupSources.Count; sourceIndex++)
        {
            var sourceGlyphs = sourceIndex == 0 ? glyphs : variantGlyphsByName[groupSources[sourceIndex].Tag];
            foreach (var g in sourceGlyphs)
            {
                renderGlyphs.Add(new RasterizedGlyph
                {
                    Codepoint = AtlasGroupBuilder.MakeCompositeId(sourceIndex, g.Codepoint),
                    GlyphIndex = g.GlyphIndex,
                    BitmapData = g.BitmapData,
                    Width = g.Width,
                    Height = g.Height,
                    Pitch = g.Pitch,
                    Metrics = g.Metrics,
                    Format = g.Format
                });
            }
        }

        var pages = AtlasBuilder.Build(renderGlyphs, shared, padding, encoder);
        metrics?.End();

        metrics?.Begin("ModelAssembly");
        var model = BmFontModelBuilder.Build(fontInfo, glyphs, shared, options,
            placementOverride: placementsByTag["primary"],
            effectiveSize: rasterResult.EffectiveSize,
            rasterizerFontMetrics: rasterResult.RasterizerFontMetrics,
            rasterizerKerningPairs: rasterResult.RasterizerKerningPairs);
        model = BmFontModelBuilder.WithVariantLinks(model, variantOf: null,
            variants: options.Variants.Select(v => v.Name).ToList());

        var variantModelsDict = new Dictionary<string, BmFontModel>();
        var variantPagesDict = new Dictionary<string, IReadOnlyList<AtlasPage>>();

        foreach (var variant in options.Variants)
        {
            var variantModel = BmFontModelBuilder.Build(fontInfo, variantGlyphsByName[variant.Name], shared, options,
                placementOverride: placementsByTag[variant.Name],
                effectiveSize: rasterResult.EffectiveSize,
                rasterizerFontMetrics: rasterResult.RasterizerFontMetrics,
                rasterizerKerningPairs: rasterResult.RasterizerKerningPairs);
            variantModel = BmFontModelBuilder.WithVariantLinks(variantModel, variantOf: fontInfo.FamilyName, variants: null);

            variantModelsDict[variant.Name] = variantModel;
            // Same physical pages as the primary -- one shared PNG.
            variantPagesDict[variant.Name] = pages;
        }
        metrics?.End();

        return new BmFontResult(model, pages, failedCodepoints, metrics, options, sourceFontFile, sourceFontName,
            variantModelsDict, variantPagesDict);
    }

    /// <summary>Generates a BMFont from raw font bytes at the given size.</summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="size">Font size in pixels.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult Generate(byte[] fontData, int size)
        => Generate(fontData, new FontGeneratorOptions { Size = size });

    /// <summary>Generates a BMFont from a font file on disk.</summary>
    /// <param name="fontPath">Path to a TTF/OTF/WOFF file.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult Generate(string fontPath, FontGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fontPath);
        return GenerateCore(File.ReadAllBytes(fontPath), options, sourceFontFile: fontPath, sourceFontName: null);
    }

    /// <summary>Generates a BMFont from a font file on disk at the given size.</summary>
    /// <param name="fontPath">Path to a TTF/OTF/WOFF file.</param>
    /// <param name="size">Font size in pixels.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult Generate(string fontPath, int size)
    {
        ArgumentNullException.ThrowIfNull(fontPath);
        return GenerateCore(File.ReadAllBytes(fontPath), new FontGeneratorOptions { Size = size }, sourceFontFile: fontPath, sourceFontName: null);
    }

    /// <summary>Generates a BMFont from a system-installed font, looked up by family name (e.g., "Arial").</summary>
    /// <param name="fontFamily">Font family name, like "Arial" or "Times New Roman".</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    /// <exception cref="FontParsingException">Thrown if the font family is not installed.</exception>
    public static BmFontResult GenerateFromSystem(string fontFamily, FontGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        options ??= new FontGeneratorOptions();

        // Try to load a style-specific variant (e.g., "Bold", "Italic", "Bold Italic")
        // to match GDI behavior: if the font family has a dedicated bold face, use it
        // directly without synthetic emboldening. If no styled variant exists, fall back
        // to the regular face and let the rasterizer apply synthetic bold/italic.
        // ForceSyntheticBold/ForceSyntheticItalic skip the variant lookup for that axis,
        // always using the regular face with synthetic styling.
        bool lookupBold = options.Bold && !options.ForceSyntheticBold;
        bool lookupItalic = options.Italic && !options.ForceSyntheticItalic;

        FontLoadResult? fontResult = null;
        bool loadedStyledVariant = false;

        // Check the font registry first (registered fonts take priority over system fonts).
        if (lookupBold && lookupItalic)
        {
            fontResult = LoadFromRegistry(fontFamily, "Bold Italic");
            if (fontResult != null)
                loadedStyledVariant = true;
        }

        if (fontResult == null && lookupBold)
        {
            fontResult = LoadFromRegistry(fontFamily, "Bold");
            if (fontResult != null)
                loadedStyledVariant = true;
        }

        if (fontResult == null && lookupItalic)
        {
            fontResult = LoadFromRegistry(fontFamily, "Italic");
            if (fontResult != null)
                loadedStyledVariant = true;
        }

        fontResult ??= LoadFromRegistry(fontFamily);

        // Fall back to system font provider if nothing was registered.
        if (fontResult == null)
        {
            if (lookupBold && lookupItalic)
            {
                fontResult = s_systemFontProvider.Value.LoadFont(fontFamily, "Bold Italic");
                if (fontResult != null)
                    loadedStyledVariant = true;
            }

            if (fontResult == null && lookupBold)
            {
                fontResult = s_systemFontProvider.Value.LoadFont(fontFamily, "Bold");
                if (fontResult != null)
                    loadedStyledVariant = true;
            }

            if (fontResult == null && lookupItalic)
            {
                fontResult = s_systemFontProvider.Value.LoadFont(fontFamily, "Italic");
                if (fontResult != null)
                    loadedStyledVariant = true;
            }

            fontResult ??= s_systemFontProvider.Value.LoadFont(fontFamily)
                ?? throw new FontParsingException($"System font '{fontFamily}' not found");
        }

        var originalFaceIndex = options.FaceIndex;
        try
        {
            options.FaceIndex = fontResult.FaceIndex;

            // When a styled variant was found (e.g., Georgia Bold), use LoadFont(data) so the
            // rasterizer gets the actual bold font bytes. Don't clear options.Bold/Italic --
            // rasterizers need them (GDI uses them in LOGFONTW to select the correct face,
            // FreeType checks style_flags to avoid double-applying, DirectWrite ignores
            // redundant simulations on already-styled faces).
            //
            // When ForceSynthetic is set, also use LoadFont(data) with the regular face so
            // rasterizers apply synthetic styling on the regular font rather than GDI's font
            // mapper silently selecting the real bold/italic face via the system font path.
            bool forceSynthetic = options.ForceSyntheticBold || options.ForceSyntheticItalic;
            var sysFamily = (loadedStyledVariant || forceSynthetic) ? null : fontFamily;
            return GenerateCore(fontResult.Data, options, sourceFontFile: null, sourceFontName: fontFamily, systemFontFamily: sysFamily);
        }
        finally
        {
            options.FaceIndex = originalFaceIndex;
        }
    }

    /// <summary>Generates a BMFont from a system-installed font at the given size.</summary>
    /// <param name="fontFamily">Font family name, like "Arial".</param>
    /// <param name="size">Font size in pixels.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult GenerateFromSystem(string fontFamily, int size)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        return GenerateFromSystem(fontFamily, new FontGeneratorOptions { Size = size });
    }

    /// <summary>Queries the estimated atlas size from raw font bytes without rasterizing.</summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>Estimated atlas dimensions and page count.</returns>
    public static AtlasSizeInfo QueryAtlasSize(byte[] fontData, FontGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        return QueryAtlasSizeCore(fontData, options);
    }

    /// <summary>Queries the estimated atlas size from a font file on disk without rasterizing.</summary>
    /// <param name="fontPath">Path to a TTF/OTF/WOFF file.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>Estimated atlas dimensions and page count.</returns>
    public static AtlasSizeInfo QueryAtlasSize(string fontPath, FontGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fontPath);
        return QueryAtlasSizeCore(File.ReadAllBytes(fontPath), options);
    }

    /// <summary>Queries the estimated atlas size from a system-installed font without rasterizing.</summary>
    /// <param name="fontFamily">Font family name, like "Arial".</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>Estimated atlas dimensions and page count.</returns>
    public static AtlasSizeInfo QueryAtlasSizeFromSystem(string fontFamily, FontGeneratorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        var fontResult = s_systemFontProvider.Value.LoadFont(fontFamily)
            ?? throw new FontParsingException($"System font '{fontFamily}' not found");
        options ??= new FontGeneratorOptions();
        options.FaceIndex = fontResult.FaceIndex;
        return QueryAtlasSizeCore(fontResult.Data, options);
    }

    /// <summary>Reads font metadata from raw font file bytes without generating a bitmap font.</summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF font file bytes.</param>
    /// <param name="faceIndex">Face index for .ttc font collections (default 0).</param>
    /// <returns>Font metadata including family name, metrics, available codepoints, and kerning pairs.</returns>
    public static Font.Models.FontInfo ReadFontInfo(byte[] fontData, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        // Auto-detect and decompress WOFF/WOFF2
        if (WoffDecompressor.IsWoff(fontData) || WoffDecompressor.IsWoff2(fontData))
            fontData = WoffDecompressor.Decompress(fontData);

        var reader = new TtfFontReader();
        return reader.ReadFont(fontData, faceIndex);
    }

    /// <summary>Reads font metadata from a font file on disk without generating a bitmap font.</summary>
    /// <param name="fontPath">Path to a TTF/OTF/WOFF font file.</param>
    /// <param name="faceIndex">Face index for .ttc font collections (default 0).</param>
    /// <returns>Font metadata including family name, metrics, available codepoints, and kerning pairs.</returns>
    public static Font.Models.FontInfo ReadFontInfo(string fontPath, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(fontPath);
        return ReadFontInfo(File.ReadAllBytes(fontPath), faceIndex);
    }

    private static AtlasSizeInfo QueryAtlasSizeCore(byte[] fontData, FontGeneratorOptions? options)
    {
        options ??= new FontGeneratorOptions();

        if (options.Size <= 0 || options.Size > 10000)
            throw new ArgumentOutOfRangeException(nameof(options), $"Size must be between 1 and 10000, was {options.Size}.");

        // Auto-detect and decompress WOFF/WOFF2
        if (WoffDecompressor.IsWoff(fontData) || WoffDecompressor.IsWoff2(fontData))
            fontData = WoffDecompressor.Decompress(fontData);

        // 1. Parse font
        var fontReader = options.FontReader ?? new TtfFontReader();
        if (fontReader is TtfFontReader ttfReader)
        {
            ttfReader.RequestedCodepoints = options.Characters.GetCodepointsHashSet();
            ttfReader.SharedFontBytes = fontData;
        }

        var fontInfo = fontReader.ReadFont(fontData, options.FaceIndex);

        // 2. Resolve character set
        var codepoints = options.Characters.Resolve(fontInfo.AvailableCodepoints).ToList();
        // FallbackCodepoint takes precedence over FallbackCharacter
        var resolvedFallback = options.FallbackCodepoint
            ?? (options.FallbackCharacter.HasValue ? (int)options.FallbackCharacter.Value : (int?)null);
        if (resolvedFallback.HasValue)
        {
            if (!codepoints.Contains(resolvedFallback.Value))
                codepoints.Add(resolvedFallback.Value);
        }

        // 3. Get metrics without rasterizing
        var rasterizer = options.Rasterizer
            ?? RasterizerFactory.Create(options.Backend);
        try
        {
            rasterizer.LoadFont(fontData, options.FaceIndex);

            // Apply variable font axes if specified.
            if (rasterizer.Capabilities.SupportsVariableFonts
                && options.VariationAxes is { Count: > 0 }
                && fontInfo.VariationAxes is { Count: > 0 })
            {
                rasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes);
            }

            var rasterOptions = RasterOptions.FromGeneratorOptions(options);
            var padding = options.Padding;
            var spacing = options.Spacing;

            var glyphRects = new List<GlyphRect>();
            foreach (var cp in codepoints)
            {
                var m = rasterizer.GetGlyphMetrics(cp, rasterOptions);
                if (m.HasValue)
                {
                    var gm = m.Value;
                    glyphRects.Add(new GlyphRect(
                        cp,
                        gm.Width + padding.Left + padding.Right + spacing.Horizontal,
                        gm.Height + padding.Up + padding.Down + spacing.Vertical));
                }
            }

            // 4. Estimate atlas size
            var sizingOptions = AtlasSizeEstimator.BuildSizingOptions(options);

            var (estWidth, estHeight) = AtlasSizeEstimator.Estimate(glyphRects, sizingOptions);

            // 5. Apply size constraints if specified.
            if (options.SizeConstraints is { } sizeConstraints)
            {
                var (cw, ch) = AtlasSizeEstimator.ApplyConstraints(
                    estWidth, estHeight, sizeConstraints, sizingOptions, glyphRects);
                estWidth = cw;
                estHeight = ch;
            }

            // 6. Calculate estimated efficiency
            long totalGlyphArea = 0;
            foreach (var r in glyphRects)
                totalGlyphArea += (long)r.Width * r.Height;

            long atlasArea = (long)estWidth * estHeight;
            var efficiency = atlasArea > 0 ? (float)totalGlyphArea / atlasArea : 0f;

            // Determine page count estimate (simple: how many full pages worth of glyph area).
            var pageCount = atlasArea > 0
                ? Math.Max(1, (int)Math.Ceiling((double)totalGlyphArea / (atlasArea * options.PackingEfficiencyHint)))
                : 1;

            return new AtlasSizeInfo
            {
                Width = estWidth,
                Height = estHeight,
                PageCount = pageCount,
                GlyphCount = glyphRects.Count,
                EstimatedEfficiency = efficiency
            };
        }
        finally
        {
            if (options.Rasterizer == null)
                rasterizer.Dispose();
        }
    }

    /// <summary>Generates a bitmap font from a config file, auto-detecting .bmfc or .hiero format.</summary>
    /// <param name="bmfcPath">Path to a configuration file. Despite the name, this accepts both <c>.bmfc</c> and <c>.hiero</c> files (auto-detected by inspecting the file content; the extension is used only as a fallback when the content is inconclusive).</param>
    /// <returns>The generated bitmap font result.</returns>
    public static BmFontResult FromConfig(string bmfcPath)
    {
        ArgumentNullException.ThrowIfNull(bmfcPath);
        var config = ConfigFormatFactory.ReadConfig(bmfcPath);
        return FromConfig(config);
    }

    /// <summary>Generates a bitmap font from a parsed configuration (from a <c>.bmfc</c> or <c>.hiero</c> file).</summary>
    /// <param name="config">The parsed configuration. <see cref="BmfcConfig"/> is the shared model for both the BMFont <c>.bmfc</c> and libGDX Hiero <c>.hiero</c> formats.</param>
    /// <returns>The generated bitmap font result.</returns>
    public static BmFontResult FromConfig(BmfcConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrEmpty(config.FontFile))
        {
            return GenerateCore(
                File.ReadAllBytes(config.FontFile),
                config.Options,
                sourceFontFile: config.FontFile,
                sourceFontName: config.FontName);
        }

        if (!string.IsNullOrEmpty(config.FontName))
        {
            return GenerateFromSystem(config.FontName, config.Options);
        }

        throw new InvalidOperationException(
            "The .bmfc config must specify either a FontFile or FontName.");
    }

    /// <summary>
    /// Loads a BMFont from a .fnt file, auto-detecting text/XML/binary format.
    /// Also loads atlas images (.png) from the same directory.
    /// </summary>
    /// <param name="fntPath">Path to the .fnt file.</param>
    /// <returns>The loaded bitmap font result with descriptor model and atlas pages.</returns>
    public static BmFontResult Load(string fntPath)
    {
        ArgumentNullException.ThrowIfNull(fntPath);
        var fntData = File.ReadAllBytes(fntPath);
        var model = BmFontReader.Read(fntData);

        var dir = Path.GetDirectoryName(fntPath) ?? ".";
        var pages = new List<Atlas.AtlasPage>();

        foreach (var pageEntry in model.Pages)
        {
            var pagePath = Path.Combine(dir, Path.GetFileName(pageEntry.File));
            if (File.Exists(pagePath))
            {
                var pngBytes = File.ReadAllBytes(pagePath);
                pages.Add(new Atlas.AtlasPage
                {
                    PageIndex = pageEntry.Id,
                    Width = model.Common.ScaleW,
                    Height = model.Common.ScaleH,
                    PixelData = pngBytes,
                    Format = PixelFormat.Rgba32,
                });
            }
        }

        return new BmFontResult(model, pages);
    }

    /// <summary>Loads a BMFont model from raw .fnt bytes. Does not load atlas images.</summary>
    /// <param name="fntData">Raw .fnt file bytes.</param>
    /// <returns>The parsed BMFont descriptor model.</returns>
    public static Output.Model.BmFontModel LoadModel(byte[] fntData)
    {
        ArgumentNullException.ThrowIfNull(fntData);
        return BmFontReader.Read(fntData);
    }

    /// <summary>Loads a BMFont model from a text-format .fnt string. Does not load atlas images.</summary>
    /// <param name="fntContent">Text-format .fnt content.</param>
    /// <returns>The parsed BMFont descriptor model.</returns>
    public static Output.Model.BmFontModel LoadModel(string fntContent)
    {
        ArgumentNullException.ThrowIfNull(fntContent);
        return BmFontReader.ReadText(fntContent);
    }

    /// <summary>Creates a fluent builder for BMFont generation.</summary>
    /// <returns>A new fluent builder instance.</returns>
    public static BmFontBuilder Builder() => new();

    /// <summary>
    /// Decides whether a non-default per-channel <see cref="ChannelConfig"/> should be honored.
    /// Honored whenever a non-default config is present — <see cref="ChannelCompositor"/> (the
    /// path this gate guards) natively supports routing outline/shadow content into individual
    /// channels, whether or not other effects are active. This is a separate mechanism from
    /// <see cref="FontGeneratorOptions.ChannelPacking"/> (grayscale-per-glyph packing via
    /// <c>ChannelPackedAtlasBuilder</c>), which remains mutually exclusive with effects via its
    /// own guard in <c>Generate</c>.
    /// </summary>
    internal static bool ShouldApplyChannelConfig(FontGeneratorOptions options) =>
        options.Channels is { IsDefault: false };

    /// <summary>Generates multiple BMFonts in batch, with optional parallelism and font caching.</summary>
    /// <param name="jobs">The batch jobs to run.</param>
    /// <param name="options">Batch options, or null for sequential execution.</param>
    /// <returns>Batch results with per-job outcomes and timing.</returns>
    public static BatchResult GenerateBatch(IReadOnlyList<BatchJob> jobs, BatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        options ??= new BatchOptions();
        var maxParallelism = options.MaxParallelism == 0 ? Environment.ProcessorCount : options.MaxParallelism;

        // Build font cache — either use provided or create temporary
        var cache = options.FontCache ?? new FontCache();

        // Pre-load all fonts into cache
        foreach (var job in jobs)
        {
            if (job.FontData != null) continue; // Already has bytes

            var key = job.FontPath ?? job.SystemFont;
            if (key == null) continue;
            if (cache.Contains(key)) continue;

            if (job.FontPath != null)
                cache.LoadFile(job.FontPath);
            else if (job.SystemFont != null)
                cache.LoadSystemFont(job.SystemFont);
        }

        var totalSw = Stopwatch.StartNew();

        if (options.AtlasMode == BatchAtlasMode.Combined)
        {
            var (combinedResults, sharedPages) = GenerateBatchCombined(jobs, cache);
            totalSw.Stop();
            return new BatchResult { Results = combinedResults, SharedPages = sharedPages, TotalElapsed = totalSw.Elapsed };
        }

        var results = new BatchJobResult[jobs.Count];

        if (maxParallelism <= 1)
        {
            // Sequential
            for (int i = 0; i < jobs.Count; i++)
                results[i] = RunBatchJob(i, jobs[i], cache);
        }
        else
        {
            // Parallel — falls back to sequential if platform doesn't support threading (e.g., WASM).
            try
            {
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallelism };
                Parallel.For(0, jobs.Count, parallelOptions, i =>
                {
                    results[i] = RunBatchJob(i, jobs[i], cache);
                });
            }
            catch (PlatformNotSupportedException)
            {
                for (int i = 0; i < jobs.Count; i++)
                    results[i] = RunBatchJob(i, jobs[i], cache);
            }
        }

        totalSw.Stop();
        return new BatchResult { Results = results, TotalElapsed = totalSw.Elapsed };
    }

    private static (BatchJobResult[] Results, IReadOnlyList<AtlasPage> SharedPages) GenerateBatchCombined(IReadOnlyList<BatchJob> jobs, FontCache cache)
    {
        // Validate: all jobs must use same TextureFormat.
        TextureFormat? requiredFormat = null;
        for (int i = 0; i < jobs.Count; i++)
        {
            var fmt = jobs[i].Options.TextureFormat;
            if (requiredFormat == null)
                requiredFormat = fmt;
            else if (fmt != requiredFormat.Value)
                throw new ArgumentException(
                    $"Combined atlas mode requires all jobs to use the same TextureFormat. " +
                    $"Job 0 uses {requiredFormat.Value}, but job {i} uses {fmt}.");
        }

        // Validate: channel packing is not supported in combined mode.
        for (int i = 0; i < jobs.Count; i++)
        {
            if (jobs[i].Options.ChannelPacking)
                throw new InvalidOperationException(
                    $"Channel packing cannot be used with combined atlas mode (job {i}). " +
                    "Combined mode packs all fonts into a single atlas, which is incompatible with channel packing.");
        }

        // 1. Rasterize all fonts.
        var rasterResults = new RasterizationResult[jobs.Count];
        for (int i = 0; i < jobs.Count; i++)
        {
            byte[] fontData = ResolveFontData(i, jobs[i], cache);
            rasterResults[i] = RasterizeFont(fontData, jobs[i].Options);
        }

        // 2. Build merged glyph list with composite IDs.
        var allGlyphs = new List<RasterizedGlyph>();
        var maxPadding = new Padding(0, 0, 0, 0);
        var maxSpacing = new Spacing(0, 0);

        for (int i = 0; i < rasterResults.Length; i++)
        {
            var jobOptions = jobs[i].Options;
            var p = jobOptions.Padding;
            var s = jobOptions.Spacing;

            maxPadding = new Padding(
                Math.Max(maxPadding.Up, p.Up),
                Math.Max(maxPadding.Right, p.Right),
                Math.Max(maxPadding.Down, p.Down),
                Math.Max(maxPadding.Left, p.Left));
            maxSpacing = new Spacing(
                Math.Max(maxSpacing.Horizontal, s.Horizontal),
                Math.Max(maxSpacing.Vertical, s.Vertical));

            foreach (var glyph in rasterResults[i].Glyphs)
            {
                var combinedId = EncodeCombinedId(i, glyph.Codepoint);
                allGlyphs.Add(new RasterizedGlyph
                {
                    Codepoint = combinedId,
                    GlyphIndex = glyph.GlyphIndex,
                    BitmapData = glyph.BitmapData,
                    Width = glyph.Width,
                    Height = glyph.Height,
                    Pitch = glyph.Pitch,
                    Metrics = glyph.Metrics,
                    Format = glyph.Format
                });
            }
        }

        // 3. Build GlyphRect list with composite IDs.
        var glyphRects = allGlyphs.Select(g => new GlyphRect(
            g.Codepoint,
            g.Width + maxPadding.Left + maxPadding.Right + maxSpacing.Horizontal,
            g.Height + maxPadding.Up + maxPadding.Down + maxSpacing.Vertical
        )).ToList();

        // 4. Estimate and pack.
        var firstOptions = jobs[0].Options;
        var sizingOptions = new AtlasSizingOptions
        {
            PackingEfficiency = firstOptions.PackingEfficiencyHint,
            PowerOfTwo = firstOptions.PowerOfTwo,
            AllowNonSquare = true,
            MaxWidth = 4096,
            MaxHeight = 4096,
        };

        var (estWidth, estHeight) = AtlasSizeEstimator.Estimate(glyphRects, sizingOptions);
        var packer = firstOptions.Packer ?? (firstOptions.PackingAlgorithm == PackingAlgorithm.Skyline
            ? new SkylinePacker()
            : new MaxRectsPacker());
        var packResult = packer.Pack(glyphRects, estWidth, estHeight);

        // 5. Build shared atlas pages.
        var encoder = firstOptions.AtlasEncoder ?? (firstOptions.TextureFormat switch
        {
            TextureFormat.Tga => new TgaEncoder(),
            TextureFormat.Dds => new DdsEncoder(),
            _ => new StbPngEncoder()
        });
        var sharedPages = AtlasBuilder.Build(allGlyphs, packResult, maxPadding, encoder);

        // 6. For each font, extract placements and build BmFontModel.
        var results = new BatchJobResult[jobs.Count];
        for (int i = 0; i < jobs.Count; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var rr = rasterResults[i];
                var jobOptions = rr.Options;

                // Extract placements for this font by decoding composite IDs.
                var placementOverride = new Dictionary<int, GlyphPlacement>();
                foreach (var placement in packResult.Placements)
                {
                    var (fontIndex, codepoint) = DecodeCombinedId(placement.Id);
                    if (fontIndex == i)
                    {
                        placementOverride[codepoint] = new GlyphPlacement(
                            codepoint, placement.PageIndex, placement.X, placement.Y);
                    }
                }

                var model = BmFontModelBuilder.Build(
                    rr.FontInfo, rr.Glyphs, packResult, jobOptions,
                    placementOverride: placementOverride,
                    effectiveSize: rr.EffectiveSize,
                    rasterizerFontMetrics: rr.RasterizerFontMetrics,
                    rasterizerKerningPairs: rr.RasterizerKerningPairs);
                var fontResult = new BmFontResult(model, sharedPages, rr.FailedCodepoints);

                sw.Stop();
                results[i] = new BatchJobResult { Index = i, Success = true, Result = fontResult, Elapsed = sw.Elapsed };
            }
            catch (Exception ex)
            {
                sw.Stop();
                results[i] = new BatchJobResult { Index = i, Success = false, Error = ex, Elapsed = sw.Elapsed };
            }
        }

        return (results, sharedPages);
    }

    private static byte[] ResolveFontData(int index, BatchJob job, FontCache cache)
    {
        if (job.FontData != null)
            return job.FontData;

        var key = job.FontPath ?? job.SystemFont
            ?? throw new ArgumentException($"Job {index}: no font source specified");
        return cache.Get(key);
    }

    private static BatchJobResult RunBatchJob(int index, BatchJob job, FontCache cache)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var fontData = ResolveFontData(index, job, cache);
            var result = Generate(fontData, job.Options);
            sw.Stop();
            return new BatchJobResult { Index = index, Success = true, Result = result, Elapsed = sw.Elapsed };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new BatchJobResult { Index = index, Success = false, Error = ex, Elapsed = sw.Elapsed };
        }
    }

    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
