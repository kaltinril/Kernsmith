using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Under <see cref="HintLevel.Normal"/> in bilevel every straight stem of a face at one size
/// comes out the same whole-pixel width, serif slabs aside: the x fit snaps each stem to the
/// standard vertical stem, and the stroke renderer draws a fitted stem as fitted. Read one row
/// above the bottom row, which for <c>i l I | H n m</c> is a stem row on every face here — under
/// H's crossbar and the arches, above the foot serifs. (The hint-parity tool's <c>stemVar</c>
/// reads the row at 75% of the box's height, which on a serif face whose x-height is four rows
/// is the foot-serif row of the 'n' and 'm', not a stem.)
/// </summary>
public class NativeBilevelStemWidthTests
{
    private const string StemGlyphs = "ilI|Hnm";

    [Theory]
    [InlineData("georgia.ttf", 10)]
    [InlineData("georgia.ttf", 11)]
    [InlineData("georgia.ttf", 12)]
    [InlineData("verdana.ttf", 11)]
    [InlineData("verdana.ttf", 12)]
    public void WindowsFont_StraightStems_ShareOneWidth(string file, int cell)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(NativeBytecodeFontTests.SystemFont(file));
        var face = native.Face!;
        float size = cell * (float)face.Head.UnitsPerEm / (face.Os2.WinAscent + (float)face.Os2.WinDescent);

        var widths = StemWidths(native, size);
        widths.Values.Distinct().Count().ShouldBe(1, $"{file} {cell} px: {Describe(widths)}");
        widths['l'].ShouldBe(1, "a 1 px stem up to the face's own 1.5 px threshold");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    public void Roboto_StraightStems_ShareOneWidth(int cell)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        float size = cell * 2048f / (2146f + 555f);

        var widths = StemWidths(native, size);
        widths.Values.Distinct().Count().ShouldBe(1, $"Roboto {cell} px: {Describe(widths)}");
    }

    /// <summary>The leftmost lit run on the row above the bottom row of each stem glyph.</summary>
    private static Dictionary<char, int> StemWidths(NativeRasterizer native, float size)
    {
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, HintLevel = HintLevel.Normal };
        var widths = new Dictionary<char, int>();
        foreach (char c in StemGlyphs)
        {
            var g = native.RasterizeGlyph(c, options).ShouldNotBeNull(c.ToString());
            g.Height.ShouldBeGreaterThanOrEqualTo(2, c.ToString());
            int row = g.Height - 2;
            int x = 0;
            while (x < g.Width && g.BitmapData[(row * g.Pitch) + x] == 0)
                x++;
            int run = 0;
            while (x + run < g.Width && g.BitmapData[(row * g.Pitch) + x + run] != 0)
                run++;
            run.ShouldBeGreaterThan(0, $"'{c}' has a stem on row {row}");
            widths[c] = run;
        }

        return widths;
    }

    private static string Describe(Dictionary<char, int> widths) => string.Join(" ", widths.Select(w => $"{w.Key}={w.Value}"));
}
