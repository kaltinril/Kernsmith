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
        var fontInfo = fontReader.ReadFont(fontData, options.FaceIndex);

        // 2. Resolve character set
        var codepoints = options.Characters.Resolve(fontInfo.AvailableCodepoints).ToList();

        // 3. Rasterize glyphs
        var rasterizer = options.Rasterizer ?? new FreeTypeRasterizer();
        try
        {
            rasterizer.LoadFont(fontData, options.FaceIndex);
            var rasterOptions = RasterOptions.FromGeneratorOptions(options);
            var glyphs = rasterizer.RasterizeAll(codepoints, rasterOptions).ToList();

            // 4. Apply post-processors
            if (options.PostProcessors != null)
            {
                foreach (var processor in options.PostProcessors)
                    glyphs = glyphs.Select(g => processor.Process(g)).ToList();
            }

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

            int totalArea = glyphRects.Sum(r => r.Width * r.Height);
            int pageSize = NextPowerOfTwo((int)Math.Sqrt(totalArea * 1.2));
            pageSize = Math.Clamp(pageSize, 64, options.MaxTextureSize);

            var packResult = packer.Pack(glyphRects, pageSize, pageSize);

            // 6. Build atlas pages
            var encoder = options.AtlasEncoder ?? new StbPngEncoder();
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

            return new BmFontResult(model, pages);
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
