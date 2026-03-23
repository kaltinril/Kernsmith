using System.Diagnostics;
using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Output;
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

    /// <summary>Generates a BMFont from raw font bytes.</summary>
    /// <param name="fontData">Raw TTF/OTF/WOFF file bytes.</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>The generated bitmap font result containing the .fnt descriptor and atlas pages.</returns>
    public static BmFontResult Generate(byte[] fontData, FontGeneratorOptions? options = null)
        => GenerateCore(fontData, options, sourceFontFile: null, sourceFontName: null);

    internal static RasterizationResult RasterizeFont(byte[] fontData, FontGeneratorOptions options)
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

        // 3. Rasterize glyphs
        var rasterizer = options.Rasterizer ?? new FreeTypeRasterizer();
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

            rasterizer.LoadFont(fontData, options.FaceIndex);

            if (rasterizer is FreeTypeRasterizer ftRasterizer)
            {
                if (options.VariationAxes is { Count: > 0 }
                    && fontInfo.VariationAxes is { Count: > 0 })
                {
                    ftRasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes);
                }

                if (options.ColorFont && options.ColorPaletteIndex != 0)
                {
                    ftRasterizer.SelectColorPalette(options.ColorPaletteIndex);
                }
            }

            var rasterOptions = RasterOptions.FromGeneratorOptions(options);

            // BMFont treats fontSize as cell height (usWinAscent + usWinDescent scaled),
            // not as em-square size (ppem). Compute the effective ppem that produces the
            // requested cell height. When MatchCharHeight is true (negative fontSize in
            // .bmfc), use fontSize directly as ppem (em-square mode).
            int effectiveSize = options.Size;
            if (!options.MatchCharHeight
                && fontInfo.Os2 is { } os2CellScale
                && os2CellScale.WinAscent + os2CellScale.WinDescent > 0)
            {
                effectiveSize = (int)(
                    (double)options.Size * fontInfo.UnitsPerEm
                    / (os2CellScale.WinAscent + os2CellScale.WinDescent));
                if (effectiveSize < 1) effectiveSize = 1;
                rasterOptions = rasterOptions with { Size = effectiveSize };
            }

            var ssLevel = Math.Clamp(options.SuperSampleLevel, 1, 4);
            var effectiveRasterOptions = ssLevel > 1
                ? rasterOptions with { Size = rasterOptions.Size * ssLevel }
                : rasterOptions;

            var glyphs = rasterizer.RasterizeAll(codepoints, effectiveRasterOptions).ToList();

            if (options.HeightPercent != 100)
            {
                var stretch = new HeightStretchPostProcessor(options.HeightPercent);
                glyphs = glyphs.Select(g => stretch.Process(g)).ToList();
            }

            if (options.CustomGlyphs is { Count: > 0 })
            {
                glyphs = ApplyCustomGlyphs(glyphs, options.CustomGlyphs, codepoints);
            }

            var effects = BuildEffects(options);
            if (effects.Count > 0)
                glyphs = glyphs.Select(g => GlyphCompositor.Composite(g, effects)).ToList();

            if (options.PostProcessors != null)
            {
                foreach (var processor in options.PostProcessors)
                {
                    if (processor is OutlinePostProcessor or GradientPostProcessor or ShadowPostProcessor)
                        continue;

                    glyphs = glyphs.Select(g => processor.Process(g)).ToList();
                }
            }

            if (ssLevel > 1)
            {
                glyphs = glyphs.Select(g => SuperSampleDownscale(g, ssLevel)).ToList();
            }

            if (options.EqualizeCellHeights && glyphs.Count > 0)
            {
                var maxHeight = glyphs.Max(g => g.Height);
                glyphs = glyphs.Select(g => EqualizeCellHeight(g, maxHeight)).ToList();
            }

            var rasterizedCodepoints = new HashSet<int>(glyphs.Select(g => g.Codepoint));
            var failedCodepoints = codepoints.Where(cp => !rasterizedCodepoints.Contains(cp)).ToList();

            return new RasterizationResult
            {
                FontInfo = fontInfo,
                Glyphs = glyphs,
                Codepoints = codepoints,
                FailedCodepoints = failedCodepoints,
                Options = options,
                EffectiveSize = effectiveSize
            };
        }
        finally
        {
            if (options.Rasterizer == null)
                rasterizer.Dispose();
        }
    }

    internal static int EncodeCombinedId(int fontIndex, int codepoint) => (fontIndex << 20) | (codepoint & 0xFFFFF);
    internal static (int FontIndex, int Codepoint) DecodeCombinedId(int combinedId) => (combinedId >> 20, combinedId & 0xFFFFF);

    private static BmFontResult GenerateCore(byte[] fontData, FontGeneratorOptions? options, string? sourceFontFile, string? sourceFontName)
    {
        ArgumentNullException.ThrowIfNull(fontData);

        options ??= new FontGeneratorOptions();

        if (options.MaxTextureWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureWidth must be positive, was {options.MaxTextureWidth}.");
        if (options.MaxTextureHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxTextureHeight must be positive, was {options.MaxTextureHeight}.");

        var metrics = options.CollectMetrics ? new PipelineMetrics() : null;

        metrics?.Begin("Rasterization");
        var rasterResult = RasterizeFont(fontData, options);
        metrics?.End();

        var fontInfo = rasterResult.FontInfo;
        var glyphs = rasterResult.Glyphs;
        var failedCodepoints = rasterResult.FailedCodepoints;

        // 5. Pack into atlas
        var packer = options.Packer ?? (options.PackingAlgorithm == PackingAlgorithm.Skyline
            ? new SkylinePacker()
            : new MaxRectsPacker());
        var padding = options.Padding;
        var spacing = options.Spacing;
        var glyphRects = glyphs.Select(g => new GlyphRect(
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
                effectiveSize: rasterResult.EffectiveSize);
            metrics?.End();

            return new BmFontResult(model, pages, failedCodepoints, metrics, options, sourceFontFile, sourceFontName);
        }

        // Build sizing options from generator options.
        var sizingOptions = new AtlasSizingOptions
        {
            PackingEfficiency = options.PackingEfficiencyHint,
            PowerOfTwo = options.AutofitTexture ? true : options.PowerOfTwo,
            AllowNonSquare = options.MaxTextureWidth != options.MaxTextureHeight,
            MaxWidth = options.MaxTextureWidth,
            MaxHeight = options.MaxTextureHeight,
            ChannelPacking = options.ChannelPacking,
            EqualizedCellHeights = options.EqualizeCellHeights,
        };

        metrics?.Begin("AtlasSizeEstimation");
        var (estWidth, estHeight) = AtlasSizeEstimator.Estimate(glyphRects, sizingOptions);
        pageWidth = estWidth;
        pageHeight = estHeight;
        metrics?.End();

        // Apply size constraints if specified.
        if (options.SizeConstraints is { } sizeConstraints)
        {
            var (cw, ch) = AtlasSizeEstimator.ApplyConstraints(
                pageWidth, pageHeight, sizeConstraints, sizingOptions, glyphRects);
            pageWidth = cw;
            pageHeight = ch;
        }

        metrics?.Begin("AtlasPacking");
        if (options.AutofitTexture)
        {
            // Verification pack: confirm the estimate fits on one page.
            var verifyResult = packer.Pack(glyphRects, pageWidth, pageHeight);
            if (verifyResult.PageCount > 1)
            {
                // One-step bump: double the smaller dimension (or next POT).
                if (pageWidth <= pageHeight)
                    pageWidth = sizingOptions.PowerOfTwo ? pageWidth * 2 : (int)(pageWidth * 1.5);
                else
                    pageHeight = sizingOptions.PowerOfTwo ? pageHeight * 2 : (int)(pageHeight * 1.5);

                pageWidth = Math.Min(pageWidth, options.MaxTextureWidth);
                pageHeight = Math.Min(pageHeight, options.MaxTextureHeight);
            }
        }

        {
            var packResult = packer.Pack(glyphRects, pageWidth, pageHeight);
            metrics?.End();

            // 6. Build atlas pages
            metrics?.Begin("AtlasEncoding");
            var encoder = options.AtlasEncoder ?? (options.TextureFormat switch
            {
                TextureFormat.Tga => (IAtlasEncoder)new TgaEncoder(),
                TextureFormat.Dds => new DdsEncoder(),
                _ => new StbPngEncoder()
            });
            IReadOnlyList<AtlasPage> pages;
            IReadOnlyDictionary<int, int>? glyphChannels = null;

            if (options.Channels is { } channelConfig && !channelConfig.IsDefault)
            {
                // Per-channel compositing: generate outline glyphs if any channel needs them.
                IReadOnlyList<RasterizedGlyph>? outlineGlyphs = null;
                var needsOutline = channelConfig.Alpha is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                    || channelConfig.Red is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                    || channelConfig.Green is ChannelContent.Outline or ChannelContent.GlyphAndOutline
                    || channelConfig.Blue is ChannelContent.Outline or ChannelContent.GlyphAndOutline;

                if (needsOutline)
                {
                    var outlineWidth = options.Outline > 0 ? options.Outline : 1;
                    var outlineProcessor = new OutlinePostProcessor(outlineWidth, options.OutlineR, options.OutlineG, options.OutlineB);
                    outlineGlyphs = glyphs.Select(g => outlineProcessor.Process(g)).ToList();

                    // Re-pack using the larger outline glyph dimensions so atlas cells
                    // are big enough to contain the outline fringe.
                    var outlineRects = outlineGlyphs.Select(g =>
                        new GlyphRect(g.Codepoint, g.Width + padding.Left + padding.Right,
                                      g.Height + padding.Up + padding.Down)).ToList();
                    packResult = packer.Pack(outlineRects, pageWidth, pageHeight);
                }

                // Build atlas pages with original glyphs for glyph channels and
                // outline glyphs for outline channels.
                pages = ChannelCompositor.Build(glyphs, outlineGlyphs, packResult, padding, channelConfig, encoder);

                if (needsOutline)
                {
                    // Use outline glyphs for the model so .fnt metrics (width, height,
                    // xoffset, yoffset, xadvance) reflect the expanded dimensions.
                    glyphs = outlineGlyphs!.ToList();
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
                effectiveSize: rasterResult.EffectiveSize);
            metrics?.End();

            return new BmFontResult(model, pages, failedCodepoints, metrics, options, sourceFontFile, sourceFontName);
        }
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
        // to the regular face and let FreeType apply synthetic bold/italic.
        byte[]? fontData = null;
        if (options.Bold && options.Italic)
        {
            fontData = s_systemFontProvider.Value.LoadFont(fontFamily, "Bold Italic");
            if (fontData != null)
            {
                options.Bold = false;
                options.Italic = false;
            }
        }

        if (fontData == null && options.Bold)
        {
            fontData = s_systemFontProvider.Value.LoadFont(fontFamily, "Bold");
            if (fontData != null)
            {
                options.Bold = false;
            }
        }

        if (fontData == null && options.Italic)
        {
            fontData = s_systemFontProvider.Value.LoadFont(fontFamily, "Italic");
            if (fontData != null)
            {
                options.Italic = false;
            }
        }

        fontData ??= s_systemFontProvider.Value.LoadFont(fontFamily)
            ?? throw new FontParsingException($"System font '{fontFamily}' not found");

        return GenerateCore(fontData, options, sourceFontFile: null, sourceFontName: fontFamily);
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
        var fontData = s_systemFontProvider.Value.LoadFont(fontFamily)
            ?? throw new FontParsingException($"System font '{fontFamily}' not found");
        return QueryAtlasSizeCore(fontData, options);
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
        var rasterizer = options.Rasterizer ?? new FreeTypeRasterizer();
        try
        {
            rasterizer.LoadFont(fontData, options.FaceIndex);

            // Apply variable font axes if specified.
            if (rasterizer is FreeTypeRasterizer ftRasterizer
                && options.VariationAxes is { Count: > 0 }
                && fontInfo.VariationAxes is { Count: > 0 })
            {
                ftRasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes);
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
            var sizingOptions = new AtlasSizingOptions
            {
                PackingEfficiency = options.PackingEfficiencyHint,
                PowerOfTwo = options.AutofitTexture ? true : options.PowerOfTwo,
                AllowNonSquare = options.MaxTextureWidth != options.MaxTextureHeight,
                MaxWidth = options.MaxTextureWidth,
                MaxHeight = options.MaxTextureHeight,
                ChannelPacking = options.ChannelPacking,
                EqualizedCellHeights = options.EqualizeCellHeights,
            };

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

    /// <summary>Generates a bitmap font from a .bmfc config file.</summary>
    /// <param name="bmfcPath">Path to the .bmfc configuration file.</param>
    /// <returns>The generated bitmap font result.</returns>
    public static BmFontResult FromConfig(string bmfcPath)
    {
        ArgumentNullException.ThrowIfNull(bmfcPath);
        var config = BmfcConfigReader.Read(bmfcPath);
        return FromConfig(config);
    }

    /// <summary>Generates a bitmap font from a parsed .bmfc config.</summary>
    /// <param name="config">The parsed .bmfc configuration.</param>
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
            var fontData = s_systemFontProvider.Value.LoadFont(config.FontName)
                ?? throw new FontParsingException($"System font '{config.FontName}' not found");
            return GenerateCore(fontData, config.Options, sourceFontFile: null, sourceFontName: config.FontName);
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
                options.ShadowR, options.ShadowG, options.ShadowB, options.ShadowOpacity));
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
        if (options.Outline > 0 && (options.Channels is null || options.Channels.IsDefault))
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
                options.GradientAngle, options.GradientMidpoint));
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
        var newMetrics = new Font.Models.GlyphMetrics(
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
            Metrics = new Font.Models.GlyphMetrics(
                BearingX: 0,
                BearingY: custom.Height,
                Advance: advance,
                Width: custom.Width,
                Height: custom.Height),
            Format = custom.Format
        };
    }

    /// <summary>Draws the outline behind the glyph using FreeType's stroker. Falls back to the original on failure.</summary>
    private static RasterizedGlyph CompositeWithFtStroker(
        FreeTypeRasterizer rasterizer, RasterizedGlyph glyph,
        RasterOptions rasterOptions, int outlineWidth,
        byte outlineR, byte outlineG, byte outlineB)
    {
        if (glyph.Width == 0 || glyph.Height == 0 || glyph.BitmapData.Length == 0)
            return glyph;

        RasterizedGlyph? outlineGlyph;
        try
        {
            outlineGlyph = rasterizer.RasterizeOutline(
                glyph.Codepoint, rasterOptions, outlineWidth, outlineR, outlineG, outlineB);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // FT_Stroker can fail for certain glyph types; fall back gracefully.
            return glyph;
        }

        if (outlineGlyph == null || outlineGlyph.Width == 0 || outlineGlyph.Height == 0)
            return glyph;

        // The outline bitmap is larger than the glyph bitmap.
        // Use the outline as the base canvas, then composite the glyph on top.
        var dstW = outlineGlyph.Width;
        var dstH = outlineGlyph.Height;
        var dst = new byte[dstW * dstH * 4];

        // Copy outline layer.
        Array.Copy(outlineGlyph.BitmapData, dst, Math.Min(outlineGlyph.BitmapData.Length, dst.Length));

        // Compute offset to center the glyph within the outline.
        var offsetX = outlineGlyph.Metrics.BearingX - glyph.Metrics.BearingX;
        var offsetY = glyph.Metrics.BearingY - outlineGlyph.Metrics.BearingY;

        // Composite original glyph on top using alpha-over.
        var srcW = glyph.Width;
        var srcH = glyph.Height;

        for (var y = 0; y < srcH; y++)
        {
            for (var x = 0; x < srcW; x++)
            {
                byte srcR, srcG, srcB, srcA;

                if (glyph.Format == PixelFormat.Rgba32)
                {
                    var si = y * glyph.Pitch + x * 4;
                    if (si + 3 >= glyph.BitmapData.Length) continue;
                    srcR = glyph.BitmapData[si];
                    srcG = glyph.BitmapData[si + 1];
                    srcB = glyph.BitmapData[si + 2];
                    srcA = glyph.BitmapData[si + 3];
                }
                else
                {
                    var si = y * glyph.Pitch + x;
                    if (si >= glyph.BitmapData.Length) continue;
                    srcR = 255;
                    srcG = 255;
                    srcB = 255;
                    srcA = glyph.BitmapData[si];
                }

                if (srcA == 0) continue;

                var dx = x + offsetX;
                var dy = y + offsetY;
                if (dx < 0 || dx >= dstW || dy < 0 || dy >= dstH) continue;

                var di = (dy * dstW + dx) * 4;
                var dstA = dst[di + 3];
                var sA = srcA / 255f;
                var dA = dstA / 255f;
                var outA = sA + dA * (1f - sA);

                if (outA > 0)
                {
                    dst[di + 0] = (byte)((srcR * sA + dst[di + 0] * dA * (1f - sA)) / outA);
                    dst[di + 1] = (byte)((srcG * sA + dst[di + 1] * dA * (1f - sA)) / outA);
                    dst[di + 2] = (byte)((srcB * sA + dst[di + 2] * dA * (1f - sA)) / outA);
                    dst[di + 3] = (byte)Math.Min(255, (int)(outA * 255));
                }
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = dst,
            Width = dstW,
            Height = dstH,
            Pitch = dstW * 4,
            Metrics = outlineGlyph.Metrics,
            Format = PixelFormat.Rgba32
        };
    }

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
            // Parallel
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallelism };
            Parallel.For(0, jobs.Count, parallelOptions, i =>
            {
                results[i] = RunBatchJob(i, jobs[i], cache);
            });
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
            TextureFormat.Tga => (IAtlasEncoder)new TgaEncoder(),
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
                    effectiveSize: rr.EffectiveSize);
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
