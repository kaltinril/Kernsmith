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
            Size = 48, Dpi = 72, ColorFont = true, EnableHinting = false, Bold = bold, Italic = italic,
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
}
