using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Color;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 — color fonts end to end on the committed fixtures: COLR version 0 output measured
/// against FreeType, COLR version 1 paint graphs checked against the font's palettes and plain
/// coverage, and bitmap-only color fonts rejected with a clear message.
/// </summary>
/// <remarks>
/// FreeType is compared with hinting off (Native has no hinter) and returns premultiplied RGBA,
/// so Native's straight RGBA is premultiplied before scoring.
/// </remarks>
public class NativeColorFontTests
{
    /// <summary>
    /// Glyphs compared against FreeType. '4' is left out on purpose: its inline layer glyph's
    /// <c>hmtx</c> left side bearing (172) disagrees with its <c>glyf</c> xMin (522), and FreeType
    /// moves the outline to honor the bearing while Native's decoder draws the outline where it
    /// is. That is a stage 1 (decode) difference that affects plain glyphs too, not a
    /// color-rendering one.
    /// </summary>
    private const string Sample = "ABGOQRSWagmos&08?";

    // ---- COLR version 0 (Bungee Color) -----------------------------------------------------

    [Theory]
    [InlineData(24f, 0)]
    [InlineData(48f, 0)]
    [InlineData(72f, 0)]
    [InlineData(48f, 3)]
    [InlineData(48f, 8)]
    public void BungeeColor_MatchesFreeType(float size, int palette)
    {
        var bytes = TestFonts.BungeeColorBytes();
        var options = new RasterOptions { Size = size, ColorFont = true, EnableHinting = false };

        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (palette != 0)
        {
            freeType.SelectColorPalette(palette);
            native.SelectColorPalette(palette);
        }

        var failures = new StringBuilder();
        foreach (char c in Sample)
        {
            var reference = freeType.RasterizeGlyph(c, options)!;
            var mine = native.RasterizeGlyph(c, options)!;

            reference.Format.ShouldBe(PixelFormat.Rgba32, $"FreeType should render '{c}' in color");
            mine.Format.ShouldBe(PixelFormat.Rgba32, $"Native should render '{c}' in color");

            Within1(failures, c, "advance", reference.Metrics.Advance, mine.Metrics.Advance);
            Within1(failures, c, "bearingX", reference.Metrics.BearingX, mine.Metrics.BearingX);
            Within1(failures, c, "bearingY", reference.Metrics.BearingY, mine.Metrics.BearingY);
            Within1(failures, c, "width", reference.Width, mine.Width);
            Within1(failures, c, "height", reference.Height, mine.Height);

            // Each channel separately, so a wrong palette color or a swapped channel cannot
            // hide behind a matching silhouette.
            var scores = ColorComparison.ChannelSsim(ColorComparison.Premultiply(mine), reference);
            if (scores.Min() < 0.95)
            {
                failures.Append(CultureInfo.InvariantCulture,
                    $"'{c}': SSIM R={scores[0]:F3} G={scores[1]:F3} B={scores[2]:F3} A={scores[3]:F3}\n");
            }
        }

        failures.ToString().ShouldBeEmpty($"Native disagrees with FreeType at {size}px, palette {palette}");
    }

    [Fact]
    public void BungeeColor_ChannelSsim_IsNotTriviallyHigh()
    {
        // Guards the metric: palette 0 (reds) against palette 1 (whites) must score badly on
        // the color channels even though the alpha silhouettes are identical.
        var bytes = TestFonts.BungeeColorBytes();
        var options = new RasterOptions { Size = 48, ColorFont = true, EnableHinting = false };

        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        var red = ColorComparison.Premultiply(native.RasterizeGlyph('B', options)!);
        native.SelectColorPalette(1);
        var white = ColorComparison.Premultiply(native.RasterizeGlyph('B', options)!);

        var scores = ColorComparison.ChannelSsim(red, white);
        scores[3].ShouldBeGreaterThan(0.999, "the palettes share layer shapes");
        scores[1].ShouldBeLessThan(0.5, "green differs: 0x09/0x95 against 0xFF/0xE8");
    }

    [Fact]
    public void Generate_BungeeColor_WithNative_ProducesAColoredRgbaAtlas()
    {
        var result = BmFont.Generate(TestFonts.BungeeColorBytes(), BungeeOptions(colorFont: true, palette: 0));

        var page = result.Pages[0];
        page.Format.ShouldBe(PixelFormat.Rgba32);
        CountColoredPixels(page.PixelData).ShouldBeGreaterThan(100, "Native should have drawn colored glyphs");
    }

    [Fact]
    public void Generate_BungeeColor_WithNative_HonoursThePaletteIndex()
    {
        var palette0 = BmFont.Generate(TestFonts.BungeeColorBytes(), BungeeOptions(colorFont: true, palette: 0));
        var palette6 = BmFont.Generate(TestFonts.BungeeColorBytes(), BungeeOptions(colorFont: true, palette: 6));

        palette6.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
        palette6.Pages[0].PixelData.ShouldNotBe(palette0.Pages[0].PixelData);
        palette6.FntText.ShouldBe(palette0.FntText, "a palette changes colors, not layout");
    }

    [Fact]
    public void Generate_BungeeColor_WithoutColorFont_StaysGrayscale()
    {
        var result = BmFont.Generate(TestFonts.BungeeColorBytes(), BungeeOptions(colorFont: false, palette: 0));
        result.Pages[0].Format.ShouldBe(PixelFormat.Grayscale8);
    }

    // ---- COLR version 1 (Reem Kufi Ink) ----------------------------------------------------
    // FreeType's backend draws COLR version 1 glyphs as plain grayscale (no paint graphs), so these
    // compare against the font's own data and against plain coverage of the same glyphs.

    /// <summary>Arabic marks and digits the cmap maps straight to version 1 color glyphs.</summary>
    private const string ReemColorSample = "١٤؟ءَّ۵";

    [Fact]
    public void ReemKufiInk_Version1Glyphs_RenderFromThePalette()
    {
        // Palette 0 is black, #8E0B14 and #C7060A; palette 1 is black, #404040 and #808080.
        var palettes = new[]
        {
            new HashSet<(byte, byte, byte)> { (0, 0, 0), (0x8E, 0x0B, 0x14), (0xC7, 0x06, 0x0A) },
            new HashSet<(byte, byte, byte)> { (0, 0, 0), (0x40, 0x40, 0x40), (0x80, 0x80, 0x80) },
        };

        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());
        for (int palette = 0; palette < 2; palette++)
        {
            if (palette != 0)
                native.SelectColorPalette(palette);

            int colored = 0;
            foreach (char c in ReemColorSample)
            {
                var glyph = native.RasterizeGlyph(c, new RasterOptions { Size = 48, ColorFont = true })!;
                glyph.Format.ShouldBe(PixelFormat.Rgba32, $"U+{(int)c:X4} should render in color");

                for (int i = 0; i < glyph.BitmapData.Length; i += 4)
                {
                    if (glyph.BitmapData[i + 3] != 255)
                        continue;
                    var rgb = (glyph.BitmapData[i], glyph.BitmapData[i + 1], glyph.BitmapData[i + 2]);
                    palettes[palette].ShouldContain(rgb, $"U+{(int)c:X4}, palette {palette}");
                    if (rgb != (0, 0, 0))
                        colored++;
                }
            }

            colored.ShouldBeGreaterThan(100, $"palette {palette} should show its non-black entries");
        }
    }

    [Fact]
    public void ReemKufiInk_ClipBox_SetsTheCanvas()
    {
        // one-ar has the clip box (200, 0)–(300, 600) on a 1000-unit em: at 50px, x 10..15, y 0..30.
        var options = new RasterOptions { Size = 50, ColorFont = true };
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());

        var glyph = native.RasterizeGlyph('١', options)!;
        glyph.Metrics.BearingX.ShouldBe(10);
        glyph.Width.ShouldBe(5);
        glyph.Metrics.BearingY.ShouldBe(30);
        glyph.Height.ShouldBe(30);
        native.GetGlyphMetrics('١', options).ShouldBe(glyph.Metrics);
    }

    [Theory]
    [InlineData(32f)]
    [InlineData(64f)]
    public void ReemKufiInk_ColorSilhouette_MatchesFreeTypePlainCoverage(float size)
    {
        // The base glyph outlines trace their color layers, so the color glyph's alpha is the
        // shape FreeType draws without color. Shadda (U+0651) is left out: its base outline is
        // a one-contour simplification of its five-contour layer, too coarse to compare at 32px.
        var bytes = TestFonts.ReemKufiInkBytes();
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);

        foreach (char c in ReemColorSample.Replace("ّ", string.Empty, StringComparison.Ordinal))
        {
            var color = native.RasterizeGlyph(c, new RasterOptions { Size = size, ColorFont = true })!;
            var plain = freeType.RasterizeGlyph(c, new RasterOptions { Size = size, EnableHinting = false })!;

            AlphaSsim(color, plain).ShouldBeGreaterThan(0.85, $"U+{(int)c:X4} at {size}px");
        }
    }

    [Fact]
    public void ReemKufiInk_GradientGlyphs_RenderGradients()
    {
        // The gradients sit on glyphs GSUB reaches (not the cmap), so render by glyph index.
        var bytes = TestFonts.ReemKufiInkBytes();
        var face = NativeFontFace.Load(bytes);
        var pipeline = new GlyphPipeline(OutlineDecoders.ForFace(face)!, face.Head.UnitsPerEm);
        var colors = ColorGlyphs.TryLoad(face.Tables, face.Maxp.NumGlyphs)!;
        var table = ColrV1Table.Parse(face.Tables.GetTable("COLR"))!;
        var options = new RasterOptions { Size = 64, ColorFont = true };
        var renderer = GlyphRenderers.SelectForFace(options, colors, pipeline);

        var found = new HashSet<Type>();
        for (int r = 0; r < table.BaseGlyphCount && found.Count < 2; r++)
        {
            int glyph = table.GetBaseGlyph(r);
            colors.TryGetPaintGraph(glyph, out var graph).ShouldBeTrue();
            var gradient = Gradients(graph.Root).FirstOrDefault(g => !found.Contains(g.GetType()));
            if (gradient is null)
                continue;
            found.Add(gradient.GetType());

            var prepared = pipeline.Prepare(glyph, options, GlyphTransforms.For(options, pipeline.UnitsPerEm));
            var rendered = renderer.Render(0, glyph, prepared, options);
            rendered.Format.ShouldBe(PixelFormat.Rgba32);
            renderer.Measure(prepared, options).ShouldBe(rendered.Metrics);

            var distinct = new HashSet<int>();
            for (int i = 0; i < rendered.BitmapData.Length; i += 4)
            {
                if (rendered.BitmapData[i + 3] == 255)
                    distinct.Add((rendered.BitmapData[i] << 16) | (rendered.BitmapData[i + 1] << 8) | rendered.BitmapData[i + 2]);
            }

            distinct.Count.ShouldBeGreaterThan(8, $"glyph {glyph} ({gradient.GetType().Name}) should show a gradient");
        }

        found.ShouldBe([typeof(LinearGradientPaint), typeof(RadialGradientPaint)], ignoreOrder: true);
    }

    [Fact]
    public void ReemKufiInk_LatinGlyphsWithoutColorRecords_StayPlainCoverage()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());

        foreach (char c in "Aao")
        {
            var color = native.RasterizeGlyph(c, new RasterOptions { Size = 48, ColorFont = true })!;
            var plain = native.RasterizeGlyph(c, new RasterOptions { Size = 48 })!;

            color.Format.ShouldBe(PixelFormat.Grayscale8);
            color.BitmapData.ShouldBe(plain.BitmapData);
            color.Metrics.ShouldBe(plain.Metrics);
        }
    }

    [Fact]
    public void Generate_ReemKufiInk_WithNativeAndColorFont_Succeeds()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            ColorFont = true,
            ColorPaletteIndex = 1
        };

        var result = BmFont.Generate(TestFonts.ReemKufiInkBytes(), options);
        result.Pages[0].PixelData.ShouldContain(b => b != 0);
    }

    // ---- Bitmap-only color fonts (Noto Color Emoji) ----------------------------------------

    [Fact]
    public void NotoColorEmoji_IsRejectedAtLoad_WithAClearMessage()
    {
        using var native = new NativeRasterizer();

        var ex = Should.Throw<RasterizationException>(() => native.LoadFont(TestFonts.NotoColorEmojiBytes()));
        ex.Message.ShouldContain("CBDT", Case.Sensitive);
        ex.Message.ShouldContain("bitmap", Case.Sensitive);
        ex.Message.ShouldContain("FreeType", Case.Sensitive);
    }

    [Fact]
    public void Generate_NotoColorEmoji_WithNative_FailsWithTheSameMessage()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x1F600 }),
            Backend = RasterizerBackend.Native,
            ColorFont = true
        };

        var ex = Should.Throw<Exception>(() => BmFont.Generate(TestFonts.NotoColorEmojiBytes(), options));
        ex.ToString().ShouldContain("CBDT", Case.Sensitive);
    }

    [Fact]
    public void NotoColorEmoji_FontInfoStillReads()
    {
        // The rejection is the Native backend's; core's own font parsing is unaffected.
        var info = BmFont.ReadFontInfo(TestFonts.NotoColorEmojiBytes());

        info.NumGlyphs.ShouldBeGreaterThan(0);
        info.HasColorGlyphs.ShouldBeTrue();
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static FontGeneratorOptions BungeeOptions(bool colorFont, int palette) => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABCabc0123&?"),
        Backend = RasterizerBackend.Native,
        ColorFont = colorFont,
        ColorPaletteIndex = palette
    };

    private static IEnumerable<GradientPaint> Gradients(ColrPaint? paint) => paint switch
    {
        GradientPaint g => [g],
        LayersPaint l => l.Layers.SelectMany(Gradients),
        GlyphClipPaint g => Gradients(g.Child),
        RectClipPaint r => Gradients(r.Child),
        TransformPaint t => Gradients(t.Child),
        CompositePaint c => Gradients(c.Source).Concat(Gradients(c.Backdrop)),
        _ => [],
    };

    /// <summary>SSIM of a color glyph's alpha against a grayscale glyph, over the union of their boxes (pen-aligned).</summary>
    private static double AlphaSsim(RasterizedGlyph color, RasterizedGlyph gray)
    {
        int left = Math.Min(color.Metrics.BearingX, gray.Metrics.BearingX);
        int top = Math.Max(color.Metrics.BearingY, gray.Metrics.BearingY);
        int right = Math.Max(color.Metrics.BearingX + color.Width, gray.Metrics.BearingX + gray.Width);
        int bottom = Math.Min(color.Metrics.BearingY - color.Height, gray.Metrics.BearingY - gray.Height);
        int width = right - left, height = top - bottom;

        var a = new byte[width * height];
        var b = new byte[width * height];
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int x = left + col, y = top - row;
                a[(row * width) + col] = SampleAt(color, x, y, 4, 3);
                b[(row * width) + col] = SampleAt(gray, x, y, 1, 0);
            }
        }

        return ImageSimilarity.Ssim(a, b, width, height);
    }

    private static byte SampleAt(RasterizedGlyph glyph, int x, int y, int bytesPerPixel, int channel)
    {
        int col = x - glyph.Metrics.BearingX, row = glyph.Metrics.BearingY - y;
        return col < 0 || row < 0 || col >= glyph.Width || row >= glyph.Height
            ? (byte)0
            : glyph.BitmapData[(row * glyph.Pitch) + (col * bytesPerPixel) + channel];
    }

    private static int CountColoredPixels(byte[] rgba)
    {
        int count = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] > 0 && (rgba[i] != rgba[i + 1] || rgba[i + 1] != rgba[i + 2]))
                count++;
        }

        return count;
    }

    private static void Within1(StringBuilder failures, char c, string what, int expected, int actual)
    {
        if (Math.Abs(expected - actual) > 1)
            failures.Append(CultureInfo.InvariantCulture, $"'{c}' {what}: FreeType {expected}, Native {actual}\n");
    }
}
