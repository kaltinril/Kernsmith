using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="HintLevel.Normal"/> + <see cref="AntiAliasMode.None"/> end to end: the fitted
/// outline is drawn by <see cref="MonoStrokeRasterizer"/>, so at 8-12 px bowls keep their
/// counters, apertures stay open, thin strokes are one pixel and never break, and the stroke
/// widths of round letters match the straight ones. Every other level keeps the original
/// threshold rendering.
/// </summary>
public class NativeMonoStrokeTests
{
    /// <summary>Roboto's em size for a BMFont cell of the given height (cell × upem / (winAscent + winDescent)).</summary>
    private static float RobotoCell(float cell) => cell * 2048f / (2146f + 555f);

    /// <summary>NotoSans-Regular-Latin's em size for a cell height.</summary>
    private static float NotoCell(float cell) => cell * 1000f / (1069f + 293f);

    private static RasterOptions Mono(float size, HintLevel level = HintLevel.Normal) =>
        new() { Size = size, AntiAlias = AntiAliasMode.None, HintLevel = level };

    [Fact]
    public void OnlyNormal_RendersMonoAsStrokes()
    {
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.Normal)).ShouldBe(MonoRendering.Strokes);
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.Full)).ShouldBe(MonoRendering.Strokes);
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.Default)).ShouldBe(MonoRendering.Strokes, "the default hints at Normal");
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.Default) with { EnableHinting = false }).ShouldBe(MonoRendering.Threshold);
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.None)).ShouldBe(MonoRendering.Threshold);
        CoverageRenderer.MonoRenderingFor(Mono(10f, HintLevel.Light)).ShouldBe(MonoRendering.Threshold);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(14f)]
    public void Roboto_BowlsKeepTheirCounters(float cell)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in "odbpqgOD0Q")
            Holes(native.RasterizeGlyph(c, Mono(RobotoCell(cell)))!).ShouldBe(1, $"'{c}' at a {cell} px cell");
        foreach (char c in "8B")
            Holes(native.RasterizeGlyph(c, Mono(RobotoCell(cell)))!).ShouldBe(2, $"'{c}' at a {cell} px cell");
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    public void Roboto_AperturesStayOpen(float cell)
    {
        // A 'c' whose terminals meet reads as an 'o'; the 50% threshold closed it at these
        // sizes, and so did the fitter's whole-pixel terminals. The stroke renderer opens the
        // aperture again (the terminal holding the least ink is dropped, as a pixel artist
        // drops it). 'G', 's' and 'S' are not here: at 10 px the fitted outline itself closes
        // them (the terminal lands on the bowl), which the renderer cannot know is wrong.
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in "cC")
            Holes(native.RasterizeGlyph(c, Mono(RobotoCell(cell)))!).ShouldBe(0, $"'{c}' at a {cell} px cell");
        Pixels(native.RasterizeGlyph('c', Mono(RobotoCell(cell)))!).ShouldNotBe(Pixels(native.RasterizeGlyph('o', Mono(RobotoCell(cell)))!));
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(14f)]
    public void Roboto_EveryLetterIsOnePiece(float cell)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghklmnopqrstuvwxyz0123456789")
            Components(native.RasterizeGlyph(c, Mono(RobotoCell(cell)))!).ShouldBe(1, $"'{c}' at a {cell} px cell");
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    public void Noto_DiagonalsAndArmsStayAttached(float cell)
    {
        // The 'z' diagonal at 8 ppem is a hair past 45°, the 'r' arm leaves its stem through
        // a sliver, the 'O' shoulder passes between pixel centres: each of these lost a pixel
        // in an earlier cut of the stroke rules.
        using var native = Load(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, "NotoSans-Regular-Latin.ttf")));
        foreach (char c in "zrOnkvwxyAVWXZ")
            Components(native.RasterizeGlyph(c, Mono(NotoCell(cell)))!).ShouldBe(1, $"'{c}' at a {cell} px cell");
        Holes(native.RasterizeGlyph('O', Mono(NotoCell(cell)))!).ShouldBe(1);
    }

    [Fact]
    public void Roboto12_RoundLettersAreAsThinAsStraightOnes()
    {
        // The stroke-width histogram of the bowls: inside their top and bottom rows, every
        // run is one pixel — the same width as the 'l' and 'n' stems — so a line of mono text
        // has an even weight.
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in "oOD")
        {
            var glyph = native.RasterizeGlyph(c, Mono(RobotoCell(12f)))!;
            var runs = InteriorRuns(glyph);
            runs.ShouldAllBe(r => r == 1, $"'{c}' runs {string.Join(",", runs)}");
        }

        // A stemmed bowl has one row where the bowl's top or bottom meets the stem (a run of
        // three); what it must not have is the "##.##" shoulder, a run of exactly two.
        foreach (char c in "dbpq")
        {
            var glyph = native.RasterizeGlyph(c, Mono(RobotoCell(12f)))!;
            var runs = InteriorRuns(glyph);
            runs.ShouldNotContain(2, $"'{c}' runs {string.Join(",", runs)}");
        }

        native.RasterizeGlyph('l', Mono(RobotoCell(12f)))!.Width.ShouldBe(1);
    }

    [Fact]
    public void Metrics_MatchTheStrokeBitmap()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in "aegos08BRS@&%cz")
        {
            var glyph = native.RasterizeGlyph(c, Mono(RobotoCell(10f)))!;
            var measured = native.GetGlyphMetrics(c, Mono(RobotoCell(10f)))!.Value;
            measured.ShouldBe(glyph.Metrics, $"'{c}'");
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static NativeRasterizer Load(byte[] font)
    {
        var native = new NativeRasterizer();
        native.LoadFont(font);
        return native;
    }

    private static string Pixels(RasterizedGlyph g) => $"{g.Width}x{g.Height}:" + Convert.ToHexString(g.BitmapData);

    private static List<int> InteriorRuns(RasterizedGlyph g)
    {
        var runs = new List<int>();
        for (int y = 1; y + 1 < g.Height; y++)
        {
            int run = 0;
            for (int x = 0; x <= g.Width; x++)
            {
                if (x < g.Width && g.BitmapData[(y * g.Pitch) + x] != 0)
                {
                    run++;
                }
                else if (run > 0)
                {
                    runs.Add(run);
                    run = 0;
                }
            }
        }

        return runs;
    }

    private static int Components(RasterizedGlyph g) => Regions(g, foreground: true);

    private static int Holes(RasterizedGlyph g) => Regions(g, foreground: false);

    /// <summary>Lit 8-connected regions, or unlit 4-connected regions that do not touch the border.</summary>
    private static int Regions(RasterizedGlyph g, bool foreground)
    {
        int width = g.Width, height = g.Height;
        var seen = new bool[width * height];
        int count = 0;
        for (int start = 0; start < width * height; start++)
        {
            bool lit = g.BitmapData[((start / width) * g.Pitch) + (start % width)] != 0;
            if (lit != foreground || seen[start])
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
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || (!foreground && dx != 0 && dy != 0))
                            continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;
                        int j = (ny * width) + nx;
                        bool litJ = g.BitmapData[(ny * g.Pitch) + nx] != 0;
                        if (litJ == foreground && !seen[j])
                        {
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
                }
            }

            if (foreground || !border)
                count++;
        }

        return count;
    }
}
