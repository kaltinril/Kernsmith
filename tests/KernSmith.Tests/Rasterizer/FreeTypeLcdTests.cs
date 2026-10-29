using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// FreeType's LCD mode returns one byte per subpixel, three per pixel. The backend must hand
/// core an <see cref="PixelFormat.Rgba32"/> glyph at the true pixel width — R, G and B the
/// three subpixel coverages, A the largest — with metrics that describe that bitmap.
/// </summary>
public class FreeTypeLcdTests
{
    private static byte[] LoadRoboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData('A', true)]
    [InlineData('H', true)]
    [InlineData('W', true)]
    [InlineData('g', false)]
    [InlineData('i', false)]
    public void Lcd_Glyph_IsRgbaAtPixelWidth(char ch, bool hinting)
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());
        var gray = rasterizer.RasterizeGlyph(ch, new RasterOptions { Size = 24, Dpi = 72, EnableHinting = hinting })!;
        var lcd = rasterizer.RasterizeGlyph(ch, new RasterOptions { Size = 24, Dpi = 72, EnableHinting = hinting, AntiAlias = AntiAliasMode.Lcd })!;

        lcd.Format.ShouldBe(PixelFormat.Rgba32);
        lcd.Metrics.Width.ShouldBe(lcd.Width);
        lcd.Metrics.Height.ShouldBe(lcd.Height);
        lcd.Pitch.ShouldBe(lcd.Width * 4);
        lcd.BitmapData.Length.ShouldBe(lcd.Pitch * lcd.Height);

        // The subpixel filter may spill one pixel past the grayscale box on each side, no more.
        lcd.Width.ShouldBeInRange(gray.Width, gray.Width + 2);
        lcd.Metrics.BearingX.ShouldBeInRange(gray.Metrics.BearingX - 1, gray.Metrics.BearingX);
        lcd.Metrics.BearingY.ShouldBe(gray.Metrics.BearingY);
        lcd.Metrics.Advance.ShouldBe(gray.Metrics.Advance);
    }

    [Fact]
    public void Lcd_Pixels_CarrySubpixelCoverageWithMaxAlpha()
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadRoboto());
        var lcd = rasterizer.RasterizeGlyph('W', new RasterOptions { Size = 24, Dpi = 72, AntiAlias = AntiAliasMode.Lcd })!;
        var px = lcd.BitmapData;

        var colorFringes = 0;
        var solid = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i + 3].ShouldBe(Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
            if (px[i] != px[i + 2])
                colorFringes++;
            if (px[i + 3] == 255 && px[i] == 255 && px[i + 1] == 255 && px[i + 2] == 255)
                solid++;
        }

        // A diagonal-heavy glyph has subpixel color on its edges and white in its stems.
        colorFringes.ShouldBeGreaterThan(0);
        solid.ShouldBeGreaterThan(0);
    }
}
