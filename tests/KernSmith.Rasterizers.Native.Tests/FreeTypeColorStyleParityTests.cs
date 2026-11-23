using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Native and FreeType apply the same synthetic bold strength and italic shear to each
/// <c>COLR</c> layer, so a styled color glyph lands in about the same box on both.
/// </summary>
public class FreeTypeColorStyleParityTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StyledColorGlyph_BoxIsWithinTwoPixelsOfNative(bool bold, bool italic)
    {
        var options = new RasterOptions
        {
            Size = 48,
            Dpi = 72,
            ColorFont = true,
            EnableHinting = false,
            Bold = bold,
            Italic = italic,
        };
        var font = TestFonts.BungeeColorBytes();

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(font);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(font);

        var actual = freeType.RasterizeGlyph('A', options)!;
        var reference = native.RasterizeGlyph('A', options)!;

        Math.Abs(actual.Width - reference.Width).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(actual.Height - reference.Height).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(actual.Metrics.BearingX - reference.Metrics.BearingX).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(actual.Metrics.BearingY - reference.Metrics.BearingY).ShouldBeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// The two backends threshold outlines by different rules, so edge pixels may disagree, but
    /// the bilevel silhouette and the palette color of each shared pixel must not.
    /// </summary>
    [Theory]
    [InlineData(24)]
    [InlineData(48)]
    public void UnstyledBilevelColorGlyphs_AgreeWithNative(int size)
    {
        var options = new RasterOptions
        {
            Size = size,
            Dpi = 72,
            ColorFont = true,
            EnableHinting = false,
            AntiAlias = AntiAliasMode.None,
        };
        var font = TestFonts.BungeeColorBytes();

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(font);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(font);

        long both = 0, either = 0, sameColor = 0;
        for (var codepoint = 33; codepoint <= 126; codepoint++)
        {
            var actual = freeType.RasterizeGlyph(codepoint, options)!;
            var reference = native.RasterizeGlyph(codepoint, options)!;

            var referenceInk = new Dictionary<(int X, int Y), int>();
            for (var y = 0; y < reference.Height; y++)
            {
                for (var x = 0; x < reference.Width; x++)
                {
                    var i = (y * reference.Width + x) * 4;
                    if (reference.BitmapData[i + 3] != 0)
                    {
                        var key = (reference.Metrics.BearingX + x, reference.Metrics.BearingY - y);
                        referenceInk[key] = reference.BitmapData[i] << 16 | reference.BitmapData[i + 1] << 8 | reference.BitmapData[i + 2];
                    }
                }
            }

            var shared = 0;
            for (var y = 0; y < actual.Height; y++)
            {
                for (var x = 0; x < actual.Width; x++)
                {
                    var i = (y * actual.Width + x) * 4;
                    if (actual.BitmapData[i + 3] == 0)
                        continue;
                    either++;
                    if (referenceInk.TryGetValue((actual.Metrics.BearingX + x, actual.Metrics.BearingY - y), out var color))
                    {
                        shared++;
                        if (color == (actual.BitmapData[i] << 16 | actual.BitmapData[i + 1] << 8 | actual.BitmapData[i + 2]))
                            sameColor++;
                    }
                }
            }
            both += shared;
            either += referenceInk.Count - shared;
        }

        var overlap = (double)both / either;
        var colorMatch = (double)sameColor / both;
        overlap.ShouldBeGreaterThan(0.97, $"ink overlap {overlap:F4}, color match {colorMatch:F4}");
        colorMatch.ShouldBeGreaterThan(0.90, $"ink overlap {overlap:F4}, color match {colorMatch:F4}");
    }
}
