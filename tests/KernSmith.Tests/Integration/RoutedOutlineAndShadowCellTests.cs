using KernSmith.Output;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// With outline and shadow both routed to channels, <c>ChannelCompositor</c> draws the union of
/// the glyph, its outline and its shadow, so each packed cell (and its <c>.fnt</c> rect and
/// offsets) must be that union box: no ink outside the char rects, and the glyph where the
/// offsets put it.
/// </summary>
public sealed class RoutedOutlineAndShadowCellTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static readonly ChannelConfig Routed = new(
        Alpha: ChannelContent.Glyph,
        Red: ChannelContent.Outline,
        Green: ChannelContent.Shadow,
        Blue: ChannelContent.Zero);

    [Theory]
    [InlineData(0, 6)]
    [InlineData(5, 0)]
    [InlineData(-4, -3)]
    public void EveryInkedPixel_LiesInsideItsCharRect(int shadowX, int shadowY)
    {
        var result = Generate(shadowX, shadowY);
        var page = result.Pages[0];
        var rects = result.Model.Characters;

        var outside = 0;
        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                var i = (y * page.Width + x) * 4;
                var inked = page.PixelData[i] != 0 || page.PixelData[i + 1] != 0 || page.PixelData[i + 3] != 0;
                if (inked && !rects.Any(c => x >= c.X && x < c.X + c.Width && y >= c.Y && y < c.Y + c.Height))
                    outside++;
            }
        }

        outside.ShouldBe(0, $"shadow ({shadowX},{shadowY})");
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(-4, -3)]
    public void GlyphChannel_SitsWhereTheOffsetsSayAsWithoutEffects(int shadowX, int shadowY)
    {
        // The alpha (Glyph) channel's ink, placed by each char's offsets, must land exactly where
        // the plain font's ink lands: the union cell moves the offsets with the cell.
        var plain = Generate(0, 0, outline: 0, routed: false);
        var styled = Generate(shadowX, shadowY);

        foreach (var c in "lI")
        {
            InkTopLeft(plain, c, alphaOnly: true).ShouldBe(InkTopLeft(styled, c, alphaOnly: true), $"'{c}'");
        }
    }

    /// <summary>The first inked alpha pixel of a char, in pen coordinates (offset applied).</summary>
    private static (int X, int Y) InkTopLeft(BmFontResult result, char ch, bool alphaOnly)
    {
        var page = result.Pages[0];
        var bpp = page.Format == PixelFormat.Rgba32 ? 4 : 1;
        var c = result.Model.Characters.Single(e => e.Id == ch);
        int minX = int.MaxValue, minY = int.MaxValue;
        for (var y = 0; y < c.Height; y++)
        {
            for (var x = 0; x < c.Width; x++)
            {
                var i = ((c.Y + y) * page.Width + c.X + x) * bpp + (bpp == 4 ? 3 : 0);
                if (page.PixelData[i] == 0)
                    continue;
                minX = Math.Min(minX, x + c.XOffset);
                minY = Math.Min(minY, y + c.YOffset);
            }
        }

        return (minX, minY);
    }

    private static BmFontResult Generate(int shadowX, int shadowY, int outline = 2, bool routed = true) =>
        BmFont.Generate(Font(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("lI"),
            Rasterizer = new StbTrueTypeRasterizer(),
            Outline = outline,
            ShadowOffsetX = shadowX,
            ShadowOffsetY = shadowY,
            ShadowBlur = 0,
            Channels = routed ? Routed : null,
        });
}
