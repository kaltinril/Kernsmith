using Bmfontier.Atlas;
using Bmfontier.Font;
using Bmfontier.Output;
using Bmfontier.Rasterizer;

namespace Bmfontier;

/// <summary>
/// Main entry point for BMFont generation.
/// </summary>
public static class BmFont
{
    /// <summary>
    /// Generates a BMFont atlas from font file data.
    /// </summary>
    /// <param name="fontData">Raw font file bytes (TTF/OTF).</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>A result containing the BMFont model and atlas pages.</returns>
    public static BmFontResult Generate(byte[] fontData, FontGeneratorOptions? options = null)
    {
        options ??= new FontGeneratorOptions();

        // 0. Auto-detect and decompress WOFF/WOFF2 to standard sfnt
        if (WoffDecompressor.IsWoff(fontData) || WoffDecompressor.IsWoff2(fontData))
            fontData = WoffDecompressor.Decompress(fontData);

        // 0b. Guard: SDF + super sampling is invalid — box filter corrupts distance values.
        if (options.Sdf && options.SuperSampleLevel > 1)
            throw new InvalidOperationException(
                "SDF rendering cannot be combined with super sampling (SuperSampleLevel > 1). " +
                "The box-filter downscale corrupts signed distance field values.");

        // 0c. Auto-add OutlinePostProcessor when Outline > 0 and no ChannelConfig.
        // Without a ChannelConfig, the outline post-processor is not applied by the
        // channel compositing path, so we add it to the regular post-processor list.
        if (options.Outline > 0 && (options.Channels is null || options.Channels.IsDefault))
        {
            var ppList = options.PostProcessors != null
                ? new List<IGlyphPostProcessor>(options.PostProcessors)
                : new List<IGlyphPostProcessor>();

            // Only add if not already present.
            if (!ppList.Any(pp => pp is OutlinePostProcessor))
                ppList.Add(new OutlinePostProcessor(options.Outline, options.OutlineR, options.OutlineG, options.OutlineB));

            options.PostProcessors = ppList;
        }

        // 1. Parse font
        var fontReader = options.FontReader ?? new TtfFontReader();

        // When using the built-in reader, pass codepoint hints for subsetting
        // and share the font byte array to avoid a second copy for FreeType.
        if (fontReader is TtfFontReader ttfReader)
        {
            ttfReader.RequestedCodepoints = options.Characters.GetCodepointsHashSet();
            ttfReader.SharedFontBytes = fontData;
        }

        var fontInfo = fontReader.ReadFont(fontData, options.FaceIndex);

        // 2. Resolve character set
        var codepoints = options.Characters.Resolve(fontInfo.AvailableCodepoints).ToList();

        // Ensure fallback character is included in the codepoint list.
        if (options.FallbackCharacter.HasValue)
        {
            var fbCodepoint = (int)options.FallbackCharacter.Value;
            if (!codepoints.Contains(fbCodepoint))
                codepoints.Add(fbCodepoint);
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

            rasterizer.LoadFont(fontData, options.FaceIndex);

            // Apply variable font axes if the user specified any and the font has fvar data.
            if (rasterizer is FreeTypeRasterizer ftRasterizer)
            {
                if (options.VariationAxes is { Count: > 0 }
                    && fontInfo.VariationAxes is { Count: > 0 })
                {
                    ftRasterizer.SetVariationAxes(fontInfo.VariationAxes, options.VariationAxes);
                }

                // Select a non-default color palette for CPAL-based color fonts.
                if (options.ColorFont && options.ColorPaletteIndex != 0)
                {
                    ftRasterizer.SelectColorPalette(options.ColorPaletteIndex);
                }
            }

            var rasterOptions = RasterOptions.FromGeneratorOptions(options);

            // Match char height: two-pass rendering to scale font so tallest glyph
            // exactly matches the requested pixel height.
            if (options.MatchCharHeight)
            {
                var probeGlyphs = rasterizer.RasterizeAll(codepoints, rasterOptions).ToList();
                if (probeGlyphs.Count > 0)
                {
                    var maxRenderedHeight = probeGlyphs.Max(g => g.Height);
                    if (maxRenderedHeight > 0 && maxRenderedHeight != rasterOptions.Size)
                    {
                        var adjustedSize = (int)Math.Round((double)rasterOptions.Size * rasterOptions.Size / maxRenderedHeight);
                        if (adjustedSize < 1) adjustedSize = 1;
                        rasterOptions = rasterOptions with { Size = adjustedSize };
                    }
                }
            }

            // Super sampling: rasterize at Nx size, then downscale after post-processors.
            var ssLevel = Math.Clamp(options.SuperSampleLevel, 1, 4);
            var effectiveRasterOptions = ssLevel > 1
                ? rasterOptions with { Size = rasterOptions.Size * ssLevel }
                : rasterOptions;

            var glyphs = rasterizer.RasterizeAll(codepoints, effectiveRasterOptions).ToList();

            // 3b. Height stretch — scale glyphs vertically before other post-processors.
            if (options.HeightPercent != 100)
            {
                var stretch = new HeightStretchPostProcessor(options.HeightPercent);
                glyphs = glyphs.Select(g => stretch.Process(g)).ToList();
            }

            // 3c. Custom glyph replacement — substitute rasterized glyphs with user images.
            if (options.CustomGlyphs is { Count: > 0 })
            {
                glyphs = ApplyCustomGlyphs(glyphs, options.CustomGlyphs, codepoints);
            }

            // 4. Apply post-processors.
            // When FT_Stroker is available (FreeTypeRasterizer + outline), skip the
            // OutlinePostProcessor and use vector-based stroking for better quality.
            var useFtStroker = options.Outline > 0 && rasterizer is FreeTypeRasterizer;

            if (options.PostProcessors != null)
            {
                foreach (var processor in options.PostProcessors)
                {
                    // Skip OutlinePostProcessor when FT_Stroker will handle it.
                    if (useFtStroker && processor is OutlinePostProcessor)
                        continue;

                    glyphs = glyphs.Select(g => processor.Process(g)).ToList();
                }
            }

            // 4a-stroker. FT_Stroker outline compositing: rasterize outline per glyph,
            // then composite original glyph on top.
            if (useFtStroker && rasterizer is FreeTypeRasterizer ftRast)
            {
                glyphs = glyphs.Select(g => CompositeWithFtStroker(
                    ftRast, g, effectiveRasterOptions,
                    options.Outline, options.OutlineR, options.OutlineG, options.OutlineB)).ToList();
            }

            // 4b. Super sampling downscale (after post-processors, before packing)
            if (ssLevel > 1)
            {
                glyphs = glyphs.Select(g => SuperSampleDownscale(g, ssLevel)).ToList();
            }

            // 4c. Equalize cell heights — pad all glyphs to the maximum height
            if (options.EqualizeCellHeights && glyphs.Count > 0)
            {
                var maxHeight = glyphs.Max(g => g.Height);
                glyphs = glyphs.Select(g => EqualizeCellHeight(g, maxHeight)).ToList();
            }

            // 4d. Collect failed codepoints (requested but not rasterized)
            var rasterizedCodepoints = new HashSet<int>(glyphs.Select(g => g.Codepoint));
            var failedCodepoints = codepoints.Where(cp => !rasterizedCodepoints.Contains(cp)).ToList();

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

            var (estWidth, estHeight) = AtlasSizeEstimator.Estimate(glyphRects, sizingOptions);
            pageWidth = estWidth;
            pageHeight = estHeight;

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

            var packResult = packer.Pack(glyphRects, pageWidth, pageHeight);

            // 6. Build atlas pages
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
                }

                pages = ChannelCompositor.Build(glyphs, outlineGlyphs, packResult, padding, channelConfig, encoder);
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

            // 7. Assemble BMFont model
            var model = BmFontModelBuilder.Build(fontInfo, glyphs, packResult, options, glyphChannels);

            return new BmFontResult(model, pages, failedCodepoints);
        }
        finally
        {
            // Only dispose if we created it
            if (options.Rasterizer == null)
                rasterizer.Dispose();
        }
    }

    /// <summary>
    /// Generates a BMFont atlas from font file data with the specified size.
    /// </summary>
    public static BmFontResult Generate(byte[] fontData, int size)
        => Generate(fontData, new FontGeneratorOptions { Size = size });

    /// <summary>
    /// Generates a BMFont atlas from a font file path.
    /// </summary>
    public static BmFontResult Generate(string fontPath, FontGeneratorOptions? options = null)
        => Generate(File.ReadAllBytes(fontPath), options);

    /// <summary>
    /// Generates a BMFont atlas from a font file path with the specified size.
    /// </summary>
    public static BmFontResult Generate(string fontPath, int size)
        => Generate(File.ReadAllBytes(fontPath), new FontGeneratorOptions { Size = size });

    /// <summary>
    /// Generates a BMFont atlas from a system-installed font, looked up by family name.
    /// </summary>
    /// <param name="fontFamily">The font family name to search for (e.g., "Arial").</param>
    /// <param name="options">Generation options, or null for defaults.</param>
    /// <returns>A result containing the BMFont model and atlas pages.</returns>
    /// <exception cref="FontParsingException">Thrown when the specified font family is not found on the system.</exception>
    public static BmFontResult GenerateFromSystem(string fontFamily, FontGeneratorOptions? options = null)
    {
        options ??= new FontGeneratorOptions();
        var provider = new DefaultSystemFontProvider();
        var fontData = provider.LoadFont(fontFamily)
            ?? throw new FontParsingException($"System font '{fontFamily}' not found");
        return Generate(fontData, options);
    }

    /// <summary>
    /// Generates a BMFont atlas from a system-installed font with the specified size.
    /// </summary>
    public static BmFontResult GenerateFromSystem(string fontFamily, int size)
        => GenerateFromSystem(fontFamily, new FontGeneratorOptions { Size = size });

    /// <summary>
    /// Loads a BMFont from a .fnt file path. Auto-detects format (text, XML, or binary).
    /// Also loads atlas .png files from the same directory as raw PNG bytes.
    /// </summary>
    /// <param name="fntPath">Path to the .fnt descriptor file.</param>
    /// <returns>A result containing the parsed BMFont model and atlas pages.</returns>
    public static BmFontResult Load(string fntPath)
    {
        var fntData = File.ReadAllBytes(fntPath);
        var model = BmFontReader.Read(fntData);

        var dir = Path.GetDirectoryName(fntPath) ?? ".";
        var pages = new List<Atlas.AtlasPage>();

        foreach (var pageEntry in model.Pages)
        {
            var pagePath = Path.Combine(dir, pageEntry.File);
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

    /// <summary>
    /// Loads a BMFont model from raw .fnt data, auto-detecting the format.
    /// Does not load atlas images.
    /// </summary>
    public static Output.Model.BmFontModel LoadModel(byte[] fntData) => BmFontReader.Read(fntData);

    /// <summary>
    /// Loads a BMFont model from a text-format .fnt string.
    /// Does not load atlas images.
    /// </summary>
    public static Output.Model.BmFontModel LoadModel(string fntContent) => BmFontReader.ReadText(fntContent);

    /// <summary>
    /// Creates a fluent builder for BMFont generation.
    /// </summary>
    public static BmFontBuilder Builder() => new();

    /// <summary>
    /// Downscales a rasterized glyph by the given factor using a box filter.
    /// </summary>
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

    /// <summary>
    /// Pads a glyph's bitmap to the specified target height, centering vertically.
    /// </summary>
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

    /// <summary>
    /// Applies custom glyph images, replacing rasterized glyphs or adding new ones.
    /// </summary>
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

    /// <summary>
    /// Composites a glyph with its FT_Stroker outline: draws outline first, then glyph on top.
    /// Falls back to the original glyph if stroking fails.
    /// </summary>
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
        catch
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
