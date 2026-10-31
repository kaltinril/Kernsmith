using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// An advance that lands exactly on a half pixel rounds up, as FreeType's does — not to the
/// nearest even pixel. Both the metrics shortcut (<c>GetGlyphMetrics</c>, which reads the
/// header box) and the full pipeline (<c>RasterizeGlyph</c>) round the advance, so both are
/// checked.
/// </summary>
public class AdvanceRoundingTests
{
    public static TheoryData<string, char, int> Ties => new()
    {
        // Roboto 'D': 1344 units × 16 / 2048 = 10.5 exactly.
        { "Roboto", 'D', 11 },
        // Roboto Flex 'e' at the default instance: 1088 units × 16 / 2048 = 8.5 exactly.
        { "RobotoFlex", 'e', 9 },
    };

    [Theory]
    [MemberData(nameof(Ties))]
    public void HalfPixelAdvance_RoundsUp_InMetricsAndRendering(string font, char character, int expected)
    {
        var options = new RasterOptions { Size = 16, Dpi = 72, EnableHinting = false };
        using var native = new NativeRasterizer();
        native.LoadFont(Bytes(font));

        native.GetGlyphMetrics(character, options)!.Value.Advance.ShouldBe(expected);
        native.RasterizeGlyph(character, options)!.Metrics.Advance.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(Ties))]
    public void HalfPixelAdvance_MatchesFreeType(string font, char character, int expected)
    {
        var options = new RasterOptions { Size = 16, Dpi = 72, EnableHinting = false };
        var bytes = Bytes(font);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);

        var freeTypeAdvance = freeType.RasterizeGlyph(character, options)!.Metrics.Advance;

        freeTypeAdvance.ShouldBe(expected);
        native.RasterizeGlyph(character, options)!.Metrics.Advance.ShouldBe(freeTypeAdvance);
    }

    private static byte[] Bytes(string font) =>
        font == "Roboto" ? TestFonts.RobotoRegularBytes() : TestFonts.RobotoFlexBytes();
}
