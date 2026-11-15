using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// The outline is measured from the glyph's anti-aliased edge, not from a thresholded
/// silhouette (issue #127): a partially covered edge pixel stands for an edge that sits part of
/// the way into that pixel, so a curved apex gets a curved outline and a straight edge gets the
/// same outline wherever it falls on the pixel grid.
/// </summary>
[Collection("RasterizerFactory")]
public class OutlineCoverageDistanceTests
{
    private const int Ow = 2;

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---------------------------------------------------------------- straight edges

    /// <summary>A 12x6 block whose rows 1-5 are solid and whose top row has coverage <paramref name="fringe"/>.</summary>
    private static byte[] MakeBlockWithFringeRow(byte fringe, int width = 12, int height = 6)
    {
        var alpha = new byte[width * height];
        for (var x = 0; x < width; x++)
            alpha[x] = fringe;
        for (var i = width; i < alpha.Length; i++)
            alpha[i] = 255;
        return alpha;
    }

    /// <summary>Sum of the layer's alpha (in pixels) in <paramref name="column"/> over the rows above <paramref name="belowRow"/>.</summary>
    private static float ColumnSumAbove(byte[] rgba, int layerWidth, int column, int belowRow)
    {
        var sum = 0f;
        for (var y = 0; y < belowRow; y++)
            sum += rgba[(y * layerWidth + column) * 4 + 3] / 255f;
        return sum;
    }

    [Fact]
    public void Generate_SolidStraightEdges_GetExactlyTheOutlineWidth()
    {
        // An edge on a pixel boundary: Outline = 2 is two solid rings and nothing beyond, the
        // same pixels AntiAlias = None gives.
        var alpha = MakeBlockWithFringeRow(255);
        var layer = new OutlineEffect(Ow).Generate(alpha, 12, 6, 12, default);

        byte At(int srcX, int srcY) => layer.RgbaData[((srcY + Ow) * layer.Width + srcX + Ow) * 4 + 3];

        // Flat top, above column 6.
        At(6, -1).ShouldBe((byte)255);
        At(6, -2).ShouldBe((byte)255);
        // Vertical side, left of row 3.
        At(-1, 3).ShouldBe((byte)255);
        At(-2, 3).ShouldBe((byte)255);

        var hard = new OutlineEffect(Ow, smooth: false).Generate(alpha, 12, 6, 12, default);
        // Along the straight runs (the corners are anti-aliased in one and stepped in the other).
        for (var x = 0; x < 12; x++)
        {
            for (var y = -Ow; y < 0; y++)
                At(x, y).ShouldBe(hard.RgbaData[((y + Ow) * hard.Width + x + Ow) * 4 + 3]);
        }
        // Under the body: solid backing.
        At(6, 0).ShouldBe((byte)255);
        At(6, 3).ShouldBe((byte)255);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(240)]
    public void Generate_StraightEdgeBetweenPixels_OutlineReachesTheSameDistanceFromTheEdge(int fringe)
    {
        // A flat top whose last row is only partly covered: the true edge lies fringe/255 of a
        // pixel above the solid rows, so the outline must reach exactly that much further than it
        // does over a flat top that ends on the pixel boundary -- not a whole pixel further
        // (fringe counted as solid) and not zero (fringe ignored).
        var coverage = fringe / 255f;
        var aligned = new OutlineEffect(Ow).Generate(MakeBlockWithFringeRow(0), 12, 6, 12, default);
        var between = new OutlineEffect(Ow).Generate(MakeBlockWithFringeRow((byte)fringe), 12, 6, 12, default);

        // Outline above the first solid row (source row 1 = layer row 1 + Ow), mid-span.
        var alignedReach = ColumnSumAbove(aligned.RgbaData, aligned.Width, 6 + Ow, 1 + Ow);
        var betweenReach = ColumnSumAbove(between.RgbaData, between.Width, 6 + Ow, 1 + Ow);

        alignedReach.ShouldBe(2f, 0.01f);
        (betweenReach - alignedReach).ShouldBe(coverage, 0.02f,
            $"a top row at {coverage:P0} coverage moves the edge up by that much of a pixel");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(128)]
    [InlineData(200)]
    public void OutlinePostProcessor_StraightEdgeBetweenPixels_OutlineReachesTheSameDistanceFromTheEdge(int fringe)
    {
        // The channel-routed outline (OutlinePostProcessor) is built from the same distance.
        var coverage = fringe / 255f;

        float ReachAbove(byte topRow, int sourceRow)
        {
            var glyph = new RasterizedGlyph
            {
                Codepoint = 'x',
                GlyphIndex = 0,
                BitmapData = MakeBlockWithFringeRow(topRow),
                Width = 12,
                Height = 6,
                Pitch = 12,
                Metrics = new GlyphMetrics(0, 0, 12, 12, 6),
                Format = PixelFormat.Grayscale8,
            };
            var result = new OutlinePostProcessor(Ow).Process(glyph);

            return ColumnSumAbove(result.BitmapData, result.Width, 6 + Ow, sourceRow + Ow);
        }

        // Aligned: the top row is empty, so the outline above the solid rows (source row 1 down)
        // is 2 pixels. With a fringe row, count the rows above it (the fringe row itself
        // also holds the body) plus that whole row.
        var aligned = ReachAbove(0, sourceRow: 1);
        var between = ReachAbove((byte)fringe, sourceRow: 0) + 1f;

        aligned.ShouldBe(2f, 0.01f);
        (between - aligned).ShouldBe(coverage, 0.02f);
    }

    // ---------------------------------------------------------------- curved edges

    private const int DiscSize = 32;
    private const double DiscCx = 16.0;
    private const double DiscCy = 15.85;
    private const double DiscRadius = 12.2;

    /// <summary>An anti-aliased disc (16x16 samples per pixel): its apex is a faint, several-pixel-wide row.</summary>
    private static byte[] MakeDisc()
    {
        var alpha = new byte[DiscSize * DiscSize];
        for (var y = 0; y < DiscSize; y++)
        {
            for (var x = 0; x < DiscSize; x++)
            {
                var inside = 0;
                for (var sy = 0; sy < 16; sy++)
                {
                    for (var sx = 0; sx < 16; sx++)
                    {
                        var px = x + (sx + 0.5) / 16 - DiscCx;
                        var py = y + (sy + 0.5) / 16 - DiscCy;
                        if (px * px + py * py <= DiscRadius * DiscRadius)
                            inside++;
                    }
                }

                alpha[y * DiscSize + x] = (byte)(inside * 255 / 256);
            }
        }

        return alpha;
    }

    /// <summary>Distance from a pixel centre to the disc's true edge, in the metric of <paramref name="shape"/>.</summary>
    private static double DistanceToDisc(int x, int y, OutlineShape shape)
    {
        double px = x + 0.5 - DiscCx, py = y + 0.5 - DiscCy;
        var best = double.MaxValue;
        for (var i = 0; i < 4000; i++)
        {
            var angle = i * Math.PI * 2 / 4000;
            var dx = Math.Abs(px - DiscRadius * Math.Cos(angle));
            var dy = Math.Abs(py - DiscRadius * Math.Sin(angle));
            var d = shape switch
            {
                OutlineShape.Round => Math.Sqrt(dx * dx + dy * dy),
                OutlineShape.Square => Math.Max(dx, dy),
                _ => dx + dy,
            };
            best = Math.Min(best, d);
        }

        return best;
    }

    [Theory]
    [InlineData(OutlineShape.Round, 1)]
    [InlineData(OutlineShape.Round, 2)]
    [InlineData(OutlineShape.Round, 3)]
    [InlineData(OutlineShape.Square, 2)]
    [InlineData(OutlineShape.Plus, 2)]
    public void Generate_AntiAliasedDisc_OutlineFollowsTheArc(OutlineShape shape, int ow)
    {
        // The outline of a disc is that disc grown by the outline width, so every pixel outside
        // the glyph has a known alpha: the share of it within the outline width of the true
        // circle, which for a pixel whose centre is d from the circle is ow + 0.5 - d.
        var alpha = MakeDisc();
        var layer = new OutlineEffect(ow, shape: shape).Generate(alpha, DiscSize, DiscSize, DiscSize, default);

        var worst = 0.0;
        var worstAt = "";
        for (var y = -ow; y < DiscSize + ow; y++)
        {
            for (var x = -ow; x < DiscSize + ow; x++)
            {
                var inSource = x >= 0 && y >= 0 && x < DiscSize && y < DiscSize;
                if (inSource && alpha[y * DiscSize + x] > 0)
                    continue; // under the body: backing, not ring

                var ideal = Math.Clamp(255.0 * (ow + 0.5 - DistanceToDisc(x, y, shape)), 0, 255);
                var actual = layer.RgbaData[((y + ow) * layer.Width + x + ow) * 4 + 3];
                var error = Math.Abs(actual - ideal);
                if (error > worst)
                {
                    worst = error;
                    worstAt = $"({x},{y}) actual {actual} ideal {ideal:F0}";
                }
            }
        }

        // Distances run between pixel centres, which is exact across a row or column and up to
        // about a quarter of a pixel off along a diagonal; a thresholded silhouette is half a
        // pixel or more off wherever the edge leaves the pixel grid.
        worst.ShouldBeLessThan(80.0, $"worst pixel {worstAt}");
    }

    [Fact]
    public void Generate_AntiAliasedDisc_ApexIsNotAFlatCap()
    {
        // Issue #127 in one picture: the disc's top row is a faint run several pixels wide. Two
        // rows above it the outline must be as faint as that row and fade toward its ends like
        // the arc does, instead of being one flat half-opaque bar across the whole run.
        var alpha = MakeDisc();
        var topRow = Enumerable.Range(0, DiscSize).First(y => Enumerable.Range(0, DiscSize).Any(x => alpha[y * DiscSize + x] > 0));
        var run = Enumerable.Range(0, DiscSize).Where(x => alpha[topRow * DiscSize + x] > 0).ToArray();
        run.Length.ShouldBeGreaterThanOrEqualTo(5, "the fixture needs a wide faint apex");
        run.Max(x => alpha[topRow * DiscSize + x]).ShouldBeLessThan((byte)128, "the fixture needs a faint apex");

        var layer = new OutlineEffect(Ow).Generate(alpha, DiscSize, DiscSize, DiscSize, default);
        byte At(int srcX, int srcY) => layer.RgbaData[((srcY + Ow) * layer.Width + srcX + Ow) * 4 + 3];

        var capRow = run.Select(x => At(x, topRow - 2)).ToArray();
        var apex = run.Select(x => alpha[topRow * DiscSize + x]).ToArray();

        for (var i = 0; i < run.Length; i++)
        {
            Math.Abs(capRow[i] - apex[i]).ShouldBeLessThanOrEqualTo(32,
                $"the outline's top row mirrors the apex row; outline [{string.Join(",", capRow)}] apex [{string.Join(",", apex)}]");
        }

        capRow[0].ShouldBeLessThan(capRow[capRow.Length / 2],
            $"the outline's top row is higher in the middle of the apex than at its ends; row [{string.Join(",", capRow)}]");
    }

    [Fact]
    public void Generate_FaintFringe_HasNoGapBetweenBodyAndOutline()
    {
        // Composite alpha (body over outline) never dips between the solid body and the ring.
        var alpha = MakeDisc();
        var layer = new OutlineEffect(Ow).Generate(alpha, DiscSize, DiscSize, DiscSize, default);

        for (var y = 0; y < DiscSize; y++)
        {
            for (var x = 0; x < DiscSize; x++)
            {
                if (alpha[y * DiscSize + x] > 0)
                    layer.RgbaData[((y + Ow) * layer.Width + x + Ow) * 4 + 3].ShouldBe((byte)255, $"backing under body pixel ({x},{y})");
            }
        }

        // Every empty pixel that touches the body (4-neighbour) is solid ring: a 2 px outline covers it whole.
        for (var y = 1; y < DiscSize - 1; y++)
        {
            for (var x = 1; x < DiscSize - 1; x++)
            {
                if (alpha[y * DiscSize + x] > 0)
                    continue;
                var touches = alpha[y * DiscSize + x - 1] > 0 || alpha[y * DiscSize + x + 1] > 0
                    || alpha[(y - 1) * DiscSize + x] > 0 || alpha[(y + 1) * DiscSize + x] > 0;
                if (touches)
                    layer.RgbaData[((y + Ow) * layer.Width + x + Ow) * 4 + 3].ShouldBe((byte)255, $"ring next to the body at ({x},{y})");
            }
        }
    }

    [Fact]
    public void Generate_NoSmoothing_GrayInputStaysBilevel()
    {
        // AntiAlias = None (#239): the outline is 0 or 255 whatever the glyph's own alpha is.
        var layer = new OutlineEffect(Ow, smooth: false).Generate(MakeDisc(), DiscSize, DiscSize, DiscSize, default);

        for (var i = 0; i < layer.Width * layer.Height; i++)
        {
            var a = layer.RgbaData[i * 4 + 3];
            (a == 0 || a == 255).ShouldBeTrue($"pixel {i} is {a}");
        }
    }

    // ---------------------------------------------------------------- real glyphs

    [Theory]
    [InlineData('o')]
    [InlineData('s')]
    [InlineData('a')]
    [InlineData('n')]
    public void Generate_RoundedGlyphTop_OutlineFollowsTheApex(char glyph)
    {
        // Roboto at 24 with a 2 px outline on FreeType: the body's top row is a run of partly
        // covered pixels, the edge crossing each at a different height. The outline ends 2 px
        // past that edge, so its top row (two rows up) is covered about as much as the body's:
        // at least as much as the pixel straight below, no more than the fullest pixel beside
        // it. The flattened outline drew the same half-opaque bar above all of them.
        var font = LoadTestFont();
        var plain = BmFont.Generate(font, new FontGeneratorOptions { Size = 24, Characters = CharacterSet.FromChars(glyph.ToString()) });
        var outlined = BmFont.Generate(font, new FontGeneratorOptions { Size = 24, Outline = Ow, Characters = CharacterSet.FromChars(glyph.ToString()) });

        var body = plain.Model.Characters.First(c => c.Id == glyph);
        var cell = outlined.Model.Characters.First(c => c.Id == glyph);
        cell.Width.ShouldBe(body.Width + 2 * Ow);
        cell.Height.ShouldBe(body.Height + 2 * Ow);

        var bodyPage = plain.Pages[body.Page];
        var cellPage = outlined.Pages[cell.Page];
        var bodyBpp = bodyPage.Format == PixelFormat.Rgba32 ? 4 : 1;
        byte Body(int x, int y) => bodyPage.PixelData[((body.Y + y) * bodyPage.Width + body.X + x) * bodyBpp + (bodyBpp - 1)];
        byte Cell(int x, int y) => cellPage.PixelData[((cell.Y + y) * cellPage.Width + cell.X + x) * 4 + 3];

        int Top(int x) => x < 0 || x >= body.Width ? 0 : Body(x, 0);
        var report = string.Join("; ", Enumerable.Range(0, body.Width).Select(x => $"x={x} body {Top(x)} outline {Cell(x + Ow, 0)}"));
        var edge = Enumerable.Range(0, body.Width).Where(x => Top(x) is >= 32 and < 224).ToArray();
        edge.Length.ShouldBeGreaterThanOrEqualTo(2, $"'{glyph}' needs a partly covered top row to test; {report}");

        foreach (var x in edge)
        {
            int outline = Cell(x + Ow, 0);
            outline.ShouldBeGreaterThanOrEqualTo(Top(x) - 40, $"'{glyph}' column {x}; {report}");
            outline.ShouldBeLessThanOrEqualTo(Math.Max(Top(x), Math.Max(Top(x - 1), Top(x + 1))) + 40, $"'{glyph}' column {x}; {report}");
        }

        // Not one flat bar: where the body's top row varies, the outline's top row does too.
        var bodySpread = edge.Max(Top) - edge.Min(Top);
        var outlineSpread = edge.Max(x => Cell(x + Ow, 0)) - edge.Min(x => Cell(x + Ow, 0));
        bodySpread.ShouldBeGreaterThanOrEqualTo(40, $"'{glyph}' needs an uneven top row to test; {report}");
        outlineSpread.ShouldBeGreaterThanOrEqualTo(bodySpread / 3, $"'{glyph}'; {report}");
    }
}
