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

            // Super sampling: rasterize at Nx size, then downscale after post-processors.
            var ssLevel = Math.Clamp(options.SuperSampleLevel, 1, 4);
            var effectiveRasterOptions = ssLevel > 1
                ? rasterOptions with { Size = rasterOptions.Size * ssLevel }
                : rasterOptions;

            var glyphs = rasterizer.RasterizeAll(codepoints, effectiveRasterOptions).ToList();

            // 4. Apply post-processors
            if (options.PostProcessors != null)
            {
                foreach (var processor in options.PostProcessors)
                    glyphs = glyphs.Select(g => processor.Process(g)).ToList();
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

            if (options.AutofitTexture)
            {
                // Autofit: find smallest power-of-2 texture that fits all glyphs on one page.
                int totalArea = glyphRects.Sum(r => r.Width * r.Height);
                int startSize = NextPowerOfTwo((int)Math.Sqrt(totalArea));
                startSize = Math.Max(startSize, 64);

                pageWidth = startSize;
                pageHeight = startSize;

                // Try progressively larger sizes until everything fits on one page.
                while (pageWidth <= options.MaxTextureWidth && pageHeight <= options.MaxTextureHeight)
                {
                    try
                    {
                        var testResult = packer.Pack(glyphRects, pageWidth, pageHeight);
                        if (testResult.PageCount <= 1) break;
                    }
                    catch (InvalidOperationException) { /* doesn't fit */ }

                    // Double the smaller dimension first for efficient use.
                    if (pageWidth <= pageHeight)
                        pageWidth *= 2;
                    else
                        pageHeight *= 2;
                }

                pageWidth = Math.Min(pageWidth, options.MaxTextureWidth);
                pageHeight = Math.Min(pageHeight, options.MaxTextureHeight);
            }
            else
            {
                int totalArea = glyphRects.Sum(r => r.Width * r.Height);
                int estSize = NextPowerOfTwo((int)Math.Sqrt(totalArea * 1.2));
                pageWidth = Math.Clamp(estSize, 64, options.MaxTextureWidth);
                pageHeight = Math.Clamp(estSize, 64, options.MaxTextureHeight);
            }

            var packResult = packer.Pack(glyphRects, pageWidth, pageHeight);

            // 6. Build atlas pages
            var encoder = options.AtlasEncoder ?? (options.TextureFormat == TextureFormat.Tga
                ? (IAtlasEncoder)new TgaEncoder()
                : new StbPngEncoder());
            IReadOnlyList<AtlasPage> pages;
            IReadOnlyDictionary<int, int>? glyphChannels = null;

            if (options.ChannelPacking)
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
                    int sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                    for (var sy = 0; sy < level; sy++)
                    {
                        for (var sx = 0; sx < level; sx++)
                        {
                            var srcIdx = (dy * level + sy) * srcPitch + (dx * level + sx) * 4;
                            if (srcIdx + 3 < glyph.BitmapData.Length)
                            {
                                sumR += glyph.BitmapData[srcIdx];
                                sumG += glyph.BitmapData[srcIdx + 1];
                                sumB += glyph.BitmapData[srcIdx + 2];
                                sumA += glyph.BitmapData[srcIdx + 3];
                            }
                        }
                    }
                    var dstIdx = (dy * dstW + dx) * 4;
                    dst[dstIdx] = (byte)(sumR / area);
                    dst[dstIdx + 1] = (byte)(sumG / area);
                    dst[dstIdx + 2] = (byte)(sumB / area);
                    dst[dstIdx + 3] = (byte)(sumA / area);
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
