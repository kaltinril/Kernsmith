using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 — color fonts end to end on the committed fixtures: COLR version 0 output measured
/// against FreeType, COLR version 1 falling back to plain coverage, and bitmap-only color fonts
/// rejected with a clear message.
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

    [Fact]
    public void ReemKufiInk_Version1Glyphs_FallBackToPlainCoverage()
    {
        // Native draws COLR version 0 layers only; a version 1 paint graph renders as the base
        // glyph's own outline, exactly as with ColorFont off.
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
