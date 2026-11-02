using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The <c>Normal</c> fitter's hairline weight in grayscale: a serif face's horizontals under
/// half a pixel (Source Serif's are 0.34-0.47 px at 7-10 ppem) are fitted at the hairline
/// weight instead of their true coverage, so a bowl's ring and an 'e' bar still read at the
/// 50% threshold and the counters they enclose survive.
/// </summary>
public class NativeHairlineTests
{
    /// <summary>Source Serif 4's em size for a BMFont cell height (upem 1000, winAscent 1036 + winDescent 335).</summary>
    private static float SerifCell(float cell) => cell * 1000f / 1371f;

    private static RasterOptions Gray(float size) => new() { Size = size, HintLevel = HintLevel.Normal };

    [Theory]
    [InlineData('o', 12f)]
    [InlineData('o', 14f)]
    [InlineData('e', 14f)]
    public void SourceSerif_RingsAndEyes_ReadAtTheHalfThreshold(char c, float cell)
    {
        // At a 12 px cell (8.75 ppem) the 'o' top and bottom arches are 0.4 px: one row each
        // at 127-128 coverage, and the ring was broken at the threshold (a hole at 64, none
        // at 128). Lifted to the hairline weight the ring closes and the counter reads.
        using var native = new NativeRasterizer();
        native.LoadFont(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, "SourceSerif4-Regular.otf")));

        var glyph = native.RasterizeGlyph(c, Gray(SerifCell(cell)))!;

        Holes(glyph, 128).ShouldBe(1, $"'{c}' at a {cell} px cell");
    }

    [Fact]
    public void Roboto_StemsAndBarsAreNotHairlines()
    {
        // Roboto's thinnest horizontals at a 9 px cell (6.8 ppem) are 0.50 px and its stems
        // 0.62: none is under the hairline limit, so the grayscale output of the sans is the
        // same as without the rule — the fitted strokes keep their own weight.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var l = native.RasterizeGlyph('l', Gray(9f * 2048f / 2701f))!;

        int column = 0;
        for (int x = 0; x < l.Width; x++)
            column = Math.Max(column, l.BitmapData[((l.Height / 2) * l.Pitch) + x]);
        // 0.62 px, darkened ×1.4 at this size by ThinStrokeDarkening: 0.62 × 1.4 × 255 = 221, not a lifted 0.6 × 1.4.
        column.ShouldBeInRange(200, 235);
    }

    /// <summary>Unlit (below <paramref name="threshold"/>) 4-connected regions that do not touch the border.</summary>
    private static int Holes(RasterizedGlyph g, int threshold)
    {
        int width = g.Width, height = g.Height;
        var seen = new bool[width * height];
        int count = 0;
        for (int start = 0; start < width * height; start++)
        {
            if (g.BitmapData[((start / width) * g.Pitch) + (start % width)] >= threshold || seen[start])
                continue;

            bool border = false;
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % width, y = i / width;
                border |= x == 0 || y == 0 || x == width - 1 || y == height - 1;
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    int j = (ny * width) + nx;
                    if (g.BitmapData[(ny * g.Pitch) + nx] < threshold && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (!border)
                count++;
        }

        return count;
    }
}
