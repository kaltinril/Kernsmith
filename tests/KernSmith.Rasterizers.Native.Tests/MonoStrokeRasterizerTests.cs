using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The bilevel stroke rasterizer behind <see cref="HintLevel.Normal"/> + <see cref="AntiAliasMode.None"/>:
/// a stroke under 1.5 px lights exactly one pixel per scanline (never doubled, never dropped),
/// so thin diagonals and curves step like a Bresenham line; wider spans are centre-sampled, so
/// whole-pixel stems come out exactly as fitted; and a counter the outline encloses stays open.
/// </summary>
public class MonoStrokeRasterizerTests
{
    // ---- thin strokes: exactly one pixel per scanline ----------------------------------------

    [Fact]
    public void ThinDiagonal_LightsOnePixelPerRowAndColumn_NoDoubles()
    {
        // A 0.8 px stroke at 45° from (0.3, 0) to (8.3, 8), whose coverage per row splits 0.56/0.57
        // in some rows (two pixels lit by a 50% threshold) and 0.45/0.45 in others (none, then
        // restored at the midpoint): a stair that steps unevenly. Rendered as a stroke it is a
        // clean diagonal — one pixel per row, one per column, no 2x2 block anywhere.
        var bitmap = Render(10, 8, Diagonal(0.3f, 0f, 8.3f, 8f, 0.8f));

        for (int y = 0; y < 8; y++)
            LitInRow(bitmap, 10, y).ShouldBe(1, $"row {y}");
        for (int x = 1; x < 9; x++)
            LitInColumn(bitmap, 10, 8, x).ShouldBeLessThanOrEqualTo(1, $"column {x}");
        Blocks2x2(bitmap, 10, 8).ShouldBe(0);
    }

    [Fact]
    public void ThinShallowDiagonal_IsEightConnected_WithRegularRuns()
    {
        // A 0.8 px stroke rising 3 rows over 12 columns: every column lit once, and the rows
        // hold the runs a Bresenham line of slope 1/4 has — 4 pixels each, the end runs partial
        // (3,4,4,1), no 2x2 block. A 50% threshold gives 0-2 pixels per column here.
        var bitmap = Render(14, 4, Diagonal(0.6f, 0.2f, 12.6f, 3.2f, 0.8f));

        for (int x = 1; x < 12; x++)
            LitInColumn(bitmap, 14, 4, x).ShouldBe(1, $"column {x}");
        var runs = new List<int>();
        for (int y = 0; y < 4; y++)
        {
            int run = LitInRow(bitmap, 14, y);
            if (run > 0)
                runs.Add(run);
        }

        runs.Count.ShouldBe(4, $"runs {string.Join(",", runs)}");
        runs.Skip(1).Take(runs.Count - 2).ShouldAllBe(r => r == 4, $"runs {string.Join(",", runs)}");
        Blocks2x2(bitmap, 14, 4).ShouldBe(0);
    }

    [Fact]
    public void BarUnderOneAndAHalfPixels_IsOneRow()
    {
        // A 1.3 px bar straddling rows 3 and 4 (y 3.4-4.7): 60% and 70% coverage both pass a
        // 50% threshold, doubling a bar the fitter did not place. A stroke under 1.5 px is one
        // pixel: the row holding its centre line (4.05 -> row 4).
        var bitmap = Render(6, 7, Rect(0.5f, 3.4f, 5.5f, 4.7f));

        for (int y = 0; y < 7; y++)
            LitInRow(bitmap, 6, y).ShouldBe(y == 4 ? 5 : 0, $"row {y}");
    }

    [Fact]
    public void SmallDot_IsOnePixel()
    {
        // A 1.3 px square dot off the grid (an 'i' dot): one pixel, wherever its phase falls.
        foreach (float phase in new[] { 0f, 0.3f, 0.5f, 0.7f })
        {
            var bitmap = Render(5, 5, Rect(1f + phase, 1f + phase, 2.3f + phase, 2.3f + phase));
            bitmap.Count(b => b == 255).ShouldBe(1, $"phase {phase}");
        }
    }

    // ---- wide spans: centre sampling keeps fitted whole-pixel features exact --------------------

    [Fact]
    public void WholePixelStem_IsExactlyItsColumns()
    {
        var bitmap = Render(6, 4, Rect(2f, 0f, 4f, 4f));

        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 6; x++)
                bitmap[(y * 6) + x].ShouldBe((byte)(x is 2 or 3 ? 255 : 0), $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void SolidBlock_IsCentreSampled()
    {
        // A 3.4 x 2.6 block at (1.2, 1.3): the pixels whose centres fall inside, nothing more.
        var bitmap = Render(6, 5, Rect(1.2f, 1.3f, 4.6f, 3.9f));

        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                bool inside = x + 0.5f > 1.2f && x + 0.5f < 4.6f && y + 0.5f > 1.3f && y + 0.5f < 3.9f;
                bitmap[(y * 6) + x].ShouldBe((byte)(inside ? 255 : 0), $"pixel ({x},{y})");
            }
        }
    }

    [Fact]
    public void Hairline_StillKeepsOnePixelPerRow()
    {
        // 0.3 px wide inside column 2: dropout control's case, kept.
        var bitmap = Render(5, 6, Rect(2.1f, 0f, 2.4f, 6f));

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 5; x++)
                bitmap[(y * 5) + x].ShouldBe((byte)(x == 2 ? 255 : 0), $"pixel ({x},{y})");
        }
    }

    [Fact]
    public void StemMeetingAThinArch_StaysConnected()
    {
        // An 'n' shoulder: a 1 px stem (x 0-1) with a 0.9 px arch (y 0.7-1.6) leaving it through
        // a slanted underside from (1, 0.7) to (1.4, 1.6). On the row through the junction the
        // interior span is the stem plus a sliver of the arch (x 0-1.36): thin, with the stem's
        // pixel at its midpoint. The junction pixel (1,1) is the arch's own column midpoint and
        // must stay lit, or the arch comes away from the stem.
        var stem = Rect(0f, 0f, 1f, 6f);
        EdgeSegment[] arch =
        [
            new EdgeSegment(1f, 0.7f, 4f, 0.7f),
            new EdgeSegment(4f, 0.7f, 4f, 1.6f),
            new EdgeSegment(4f, 1.6f, 1.4f, 1.6f),
            new EdgeSegment(1.4f, 1.6f, 1f, 0.7f),
        ];
        var bitmap = Render(5, 6, [.. stem, .. arch]);

        bitmap[(1 * 5) + 1].ShouldBe((byte)255, "the junction pixel");
        Components(bitmap, 5, 6).ShouldBe(1);
        LitInRow(bitmap, 5, 1).ShouldBe(4, "stem plus a one-row arch");
        LitInRow(bitmap, 5, 0).ShouldBe(1, "only the stem reaches row 0");
    }

    [Fact]
    public void Bowl_ShouldersAreOnePixel_LikeItsStems()
    {
        // Arial's 'o', 'd', 'g' at a 12 px cell: an elliptical bowl whose 1.1 px wall is one
        // whole pixel on its straight sides but covers two pixels at over 50% each where it
        // turns into the top and bottom — so a 50% threshold draws "##.##" shoulders and the
        // round letters in a line come out heavier than the straight ones. Drawn as strokes,
        // every interior row of the bowl has two single-pixel runs: the stroke-width
        // histogram of the bowl is all ones, as it is for an 'l' or 'n'.
        var ring = Ellipse(cx: 3.0f, cy: 3.5f, rx: 2.6f, ry: 3.3f, wall: 1.1f);
        var strokes = Render(6, 7, ring);
        var threshold = ScanlineRasterizer.Rasterize(ring, 6, 7, antiAlias: AntiAliasMode.None).Bitmap;

        var strokeRuns = InteriorRuns(strokes, 6, 7);
        strokeRuns.ShouldAllBe(r => r == 1, $"stroke runs {string.Join(",", strokeRuns)}");
        strokeRuns.Count.ShouldBe(10, "two sides in each of the five interior rows");

        var thresholdRuns = InteriorRuns(threshold, 6, 7);
        thresholdRuns.ShouldContain(2, "the threshold doubles the shoulders, which is the defect this guards");
    }

    // ---- topology: counters survive ----------------------------------------------------------

    [Fact]
    public void Ring_WithACounterUnderAPixel_KeepsAHole()
    {
        // A 4 x 4 ring whose 1.2 px walls leave a 1.6 x 1.6 counter at (1.7, 1.7): with the
        // walls centre-sampled the counter can land on a single pixel centre or on none. The
        // outline encloses a counter, so the bitmap must enclose one too — the ring reads as
        // an 'o', not a block.
        foreach (float phase in new[] { 0f, 0.2f, 0.5f, 0.8f })
        {
            float o = 0.5f + phase;
            var edges = new List<EdgeSegment>();
            edges.AddRange(Rect(o, o, o + 4f, o + 4f));
            edges.AddRange(Reverse(Rect(o + 1.2f, o + 1.2f, o + 2.8f, o + 2.8f)));
            var bitmap = Render(7, 7, [.. edges]);

            Holes(bitmap, 7, 7).ShouldBe(1, $"phase {phase}");
        }
    }

    [Fact]
    public void Ring_WithACounterUnderHalfAPixel_KeepsAHole()
    {
        // An 'e' eye at 6 ppem: a 2.0 x 0.4 px counter under 1.2 px walls. Too thin for any
        // pixel centre, but still a counter: one pixel is opened at its middle.
        var edges = new List<EdgeSegment>();
        edges.AddRange(Rect(0.5f, 0.5f, 4.9f, 3.3f));
        edges.AddRange(Reverse(Rect(1.7f, 1.7f, 3.7f, 2.1f)));
        var bitmap = Render(6, 5, [.. edges]);

        Holes(bitmap, 6, 5).ShouldBe(1);
    }

    [Fact]
    public void SolidShape_GetsNoHole()
    {
        var bitmap = Render(6, 6, Rect(0.5f, 0.5f, 5.5f, 5.5f));

        Holes(bitmap, 6, 6).ShouldBe(0);
        bitmap.Count(b => b == 255).ShouldBe(25);
    }

    [Fact]
    public void EmptyEdges_RenderNothing()
    {
        Render(4, 4, []).ShouldAllBe(b => b == 0);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static byte[] Render(int width, int height, EdgeSegment[] edges) =>
        MonoStrokeRasterizer.Rasterize(edges, width, height, 0f, 0f);

    private static EdgeSegment[] Rect(float left, float top, float right, float bottom) =>
    [
        new EdgeSegment(left, top, right, top),
        new EdgeSegment(right, top, right, bottom),
        new EdgeSegment(right, bottom, left, bottom),
        new EdgeSegment(left, bottom, left, top),
    ];

    private static EdgeSegment[] Reverse(EdgeSegment[] edges) =>
        Enumerable.Reverse(edges).Select(e => new EdgeSegment(e.X1, e.Y1, e.X0, e.Y0)).ToArray();

    /// <summary>An elliptical ring: an outer ellipse and a reversed inner one <paramref name="wall"/> px inside it, 64 segments each.</summary>
    private static EdgeSegment[] Ellipse(float cx, float cy, float rx, float ry, float wall)
    {
        var edges = new List<EdgeSegment>();
        Add(rx, ry, clockwise: true);
        Add(rx - wall, ry - wall, clockwise: false);
        return [.. edges];

        void Add(float a, float b, bool clockwise)
        {
            const int Segments = 64;
            for (int i = 0; i < Segments; i++)
            {
                float t0 = 2f * MathF.PI * i / Segments, t1 = 2f * MathF.PI * (i + 1) / Segments;
                if (!clockwise)
                    (t0, t1) = (-t0, -t1);
                edges.Add(new EdgeSegment(cx + (a * MathF.Cos(t0)), cy + (b * MathF.Sin(t0)), cx + (a * MathF.Cos(t1)), cy + (b * MathF.Sin(t1))));
            }
        }
    }

    /// <summary>The lit run lengths of every row but the first and last.</summary>
    private static List<int> InteriorRuns(byte[] bitmap, int width, int height)
    {
        var runs = new List<int>();
        for (int y = 1; y + 1 < height; y++)
        {
            int run = 0;
            for (int x = 0; x <= width; x++)
            {
                if (x < width && bitmap[(y * width) + x] == 255)
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

    /// <summary>A stroke of the given width along the line from (x0, y0) to (x1, y1), as a closed quad.</summary>
    private static EdgeSegment[] Diagonal(float x0, float y0, float x1, float y1, float width)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt((dx * dx) + (dy * dy));
        float nx = -dy / len * width / 2f, ny = dx / len * width / 2f;
        return
        [
            new EdgeSegment(x0 + nx, y0 + ny, x1 + nx, y1 + ny),
            new EdgeSegment(x1 + nx, y1 + ny, x1 - nx, y1 - ny),
            new EdgeSegment(x1 - nx, y1 - ny, x0 - nx, y0 - ny),
            new EdgeSegment(x0 - nx, y0 - ny, x0 + nx, y0 + ny),
        ];
    }

    private static int LitInRow(byte[] bitmap, int width, int y) =>
        Enumerable.Range(0, width).Count(x => bitmap[(y * width) + x] == 255);

    private static int LitInColumn(byte[] bitmap, int width, int height, int x) =>
        Enumerable.Range(0, height).Count(y => bitmap[(y * width) + x] == 255);

    private static int Blocks2x2(byte[] bitmap, int width, int height)
    {
        int count = 0;
        for (int y = 0; y + 1 < height; y++)
        {
            for (int x = 0; x + 1 < width; x++)
            {
                if (bitmap[(y * width) + x] == 255 && bitmap[(y * width) + x + 1] == 255
                    && bitmap[((y + 1) * width) + x] == 255 && bitmap[((y + 1) * width) + x + 1] == 255)
                    count++;
            }
        }

        return count;
    }

    /// <summary>Lit components, 8-connected.</summary>
    private static int Components(byte[] bitmap, int width, int height)
    {
        var seen = new bool[width * height];
        int count = 0;
        for (int start = 0; start < bitmap.Length; start++)
        {
            if (bitmap[start] == 0 || seen[start])
                continue;

            count++;
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % width, y = i / width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;
                        int j = (ny * width) + nx;
                        if (bitmap[j] != 0 && !seen[j])
                        {
                            seen[j] = true;
                            stack.Push(j);
                        }
                    }
                }
            }
        }

        return count;
    }

    /// <summary>Background components (4-connected) not touching the border.</summary>
    private static int Holes(byte[] bitmap, int width, int height)
    {
        var seen = new bool[width * height];
        int holes = 0;
        for (int start = 0; start < bitmap.Length; start++)
        {
            if (bitmap[start] != 0 || seen[start])
                continue;

            bool touchesBorder = false;
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % width, y = i / width;
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    touchesBorder = true;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;
                    int j = (ny * width) + nx;
                    if (bitmap[j] == 0 && !seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (!touchesBorder)
                holes++;
        }

        return holes;
    }
}
