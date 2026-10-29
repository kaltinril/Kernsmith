using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Distance;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The outline layer core composites for <see cref="OutlineMode.Vector"/>: the prepared glyph
/// dilated by the outline width — every point within that distance of its filled region — as
/// 8-bit coverage.
/// </summary>
/// <remarks>
/// <para>
/// Core's bitmap outline grows the rendered ink by N pixels, measured from pixel centres in the
/// metric <see cref="OutlineShape"/> names (Euclidean, Chebyshev, Manhattan), and backs the body
/// with it. This is the same dilation measured from the true outline: the distance from a point
/// to the filled region (<see cref="DistanceShape.Boundary"/>, so overlapping contours count
/// once) in that metric — a round, square or diamond pen. The ring therefore reaches exactly N
/// pixels on every side, as the effect's does, and a square pen has no miter tip to poke past
/// it at a sharp corner.
/// </para>
/// <para>
/// Coverage. A pixel whose centre is further than half its diagonal (in the pen's metric) from
/// the pen's edge is wholly in or out. The rest — the one- to two-pixel band along the edge —
/// average 4 x 4 sub-samples, each a linear ramp a quarter pixel wide, which keeps convex and
/// concave corners of the ring within a few levels of an exact-area render. Aliased requests
/// light a pixel exactly when its centre is within N, as the effect's hard outline does.
/// </para>
/// <para>
/// INVARIANT: the layer is never the union of separately filled stroke pieces
/// (<see cref="Outlines.OutlineStroker"/>). The scanline fill integrates the winding number over
/// each pixel before clamping, so where pieces overlap at the layer's edge — the inner side of
/// every curve — it over-darkens anti-aliased pixels, measured at up to 166 of 255 levels against
/// an 8x render. A single tangent-line coverage per pixel was measured too: up to 100 levels light
/// at the ring's concave corners.
/// </para>
/// </remarks>
internal static class OutlineLayer
{
    /// <summary>Largest distance, in pixels, between a curve and the polyline distances are measured to.</summary>
    private const float Tolerance = 1f / 64f;

    /// <summary>Sub-samples per pixel side for pixels on the ring's anti-aliased edge.</summary>
    private const int Sub = 4;

    /// <summary>Renders the layer for a non-blank prepared glyph.</summary>
    /// <param name="codepoint">The character, for the result.</param>
    /// <param name="glyphIndex">The glyph, for the result and size-limit errors.</param>
    /// <param name="glyph">The glyph as <see cref="GlyphPipeline.Prepare"/> returned it.</param>
    /// <param name="width">How far the layer reaches past the glyph, in pixels.</param>
    /// <param name="shape">The pen: round, square (Chebyshev) or diamond (Manhattan).</param>
    /// <param name="heightScale">The vertical stretch the layer goes through afterwards: the pen
    /// reaches <paramref name="width"/> sideways and <paramref name="width"/> /
    /// <paramref name="heightScale"/> vertically, so the ring is even once stretched.</param>
    /// <param name="aliased">Threshold instead of anti-aliasing.</param>
    /// <returns>The layer, cropped to its ink, with the glyph's advance.</returns>
    public static RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, int width, OutlineShape shape, float heightScale, bool aliased)
    {
        var box = glyph.Box;
        int padX = width + 1;
        int padY = (int)Math.Ceiling(width / heightScale) + 1;
        int w = box.Width + (2 * padX), h = box.Height + (2 * padY);
        GlyphLimits.EnsureBitmap(glyphIndex, w, h);

        var region = DistanceShape.Build(glyph.Outline, glyph.ToPixels, box.MinX - padX, box.MinY - padY, Tolerance);
        var pen = new Pen(shape, heightScale);

        // Distances are measured in the pen's space — y multiplied by heightScale — where it is
        // round, square or a diamond of radius `width`. `band` is the furthest any point of a
        // pixel lies from its centre there, so a centre further than that from the pen's edge
        // decides the whole pixel.
        float band = pen.Distance(0.5, 0.5 * heightScale);
        float reach = width + band;
        var nearest = new float[w * h];
        Array.Fill(nearest, float.PositiveInfinity);
        foreach (var segment in region.Boundary)
            Splat(segment, pen, reach, nearest, w, h);

        var coverage = new byte[w * h];
        var inside = new bool[w];
        var scratch = new List<(double X, int Direction)>();
        var edgePixels = new List<int>();
        for (int y = 0; y < h; y++)
        {
            region.Winding.ScanRow(y, inside, scratch);
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                float d = nearest[i];
                if (inside[x] || d <= width - band || (aliased && d <= width))
                    coverage[i] = 255;
                else if (!aliased && d < width + band)
                    edgePixels.Add(i);
            }
        }

        if (edgePixels.Count > 0)
            SuperSample(edgePixels, nearest, region, pen, width, band, reach, coverage, w);

        return Crop(codepoint, glyphIndex, coverage, w, h, box.BearingX - padX, box.BearingY + padY, glyph.Advance);
    }

    /// <summary>
    /// Covers each edge pixel by the mean of its 4 x 4 sub-samples: inside the fill counts fully,
    /// and outside each ramps linearly over a quarter pixel across the pen's edge.
    /// </summary>
    /// <remarks>A sub-sample is within <paramref name="band"/> of its pixel's centre, so a segment
    /// further than the centre's own nearest distance plus twice that cannot be nearest to any
    /// sub-sample, and is skipped before the 16 distances are measured.</remarks>
    private static void SuperSample(List<int> edgePixels, float[] centreDistance, DistanceShape region, Pen pen, int width, float band, float reach, byte[] coverage, int w)
    {
        var slot = new int[coverage.Length];
        Array.Fill(slot, -1);
        for (int k = 0; k < edgePixels.Count; k++)
            slot[edgePixels[k]] = k;

        var nearest = new float[edgePixels.Count * Sub * Sub];
        Array.Fill(nearest, float.PositiveInfinity);
        foreach (var s in region.Boundary)
        {
            pen.Bounds(s, reach, w, coverage.Length / w, out int firstRow, out int lastRow, out int firstColumn, out int lastColumn);
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    int k = slot[(row * w) + column];
                    if (k < 0 || pen.ToSegment(column + 0.5, row + 0.5, s) > centreDistance[(row * w) + column] + (2 * band))
                        continue;

                    for (int sy = 0; sy < Sub; sy++)
                    {
                        double py = row + ((sy + 0.5) / Sub);
                        for (int sx = 0; sx < Sub; sx++)
                        {
                            double d = pen.ToSegment(column + ((sx + 0.5) / Sub), py, s);
                            ref float best = ref nearest[(k * Sub * Sub) + (sy * Sub) + sx];
                            if (d < best)
                                best = (float)d;
                        }
                    }
                }
            }
        }

        for (int k = 0; k < edgePixels.Count; k++)
        {
            int i = edgePixels[k];
            int row = i / w, column = i % w;
            float total = 0;
            for (int sy = 0; sy < Sub; sy++)
            {
                for (int sx = 0; sx < Sub; sx++)
                {
                    float d = nearest[(k * Sub * Sub) + (sy * Sub) + sx];
                    total += d <= width
                        ? (region.Winding.IsInside(column + ((sx + 0.5) / Sub), row + ((sy + 0.5) / Sub)) ? 1f : Math.Clamp(0.5f + ((width - d) * Sub), 0f, 1f))
                        : Math.Clamp(0.5f + ((width - d) * Sub), 0f, 1f);
                }
            }

            coverage[i] = (byte)((total * 255f / (Sub * Sub)) + 0.5f);
        }
    }

    /// <summary>Lowers <paramref name="nearest"/> for every pixel centre within <paramref name="reach"/> of the segment.</summary>
    private static void Splat(BoundarySegment s, Pen pen, float reach, float[] nearest, int width, int height)
    {
        pen.Bounds(s, reach, width, height, out int firstRow, out int lastRow, out int firstColumn, out int lastColumn);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                double d = pen.ToSegment(column + 0.5, row + 0.5, s);
                int i = (row * width) + column;
                if (d < nearest[i])
                    nearest[i] = (float)d;
            }
        }
    }

    /// <summary>A pen's distance metric, measured with y scaled by the height stretch.</summary>
    private readonly record struct Pen(OutlineShape Shape, float HeightScale)
    {
        /// <summary>The pen's norm of a vector.</summary>
        public float Distance(double x, double y) => (float)(Shape switch
        {
            OutlineShape.Square => Math.Max(Math.Abs(x), Math.Abs(y)),
            OutlineShape.Plus => Math.Abs(x) + Math.Abs(y),
            _ => Math.Sqrt((x * x) + (y * y)),
        });

        /// <summary>Distance from the point to the segment.</summary>
        public double ToSegment(double px, double py, BoundarySegment s)
        {
            double ex = px - s.X0, ey = (py - s.Y0) * HeightScale;
            double dx = s.X1 - s.X0, dy = (s.Y1 - s.Y0) * (double)HeightScale;
            return Shape switch
            {
                OutlineShape.Square => Chebyshev(ex, ey, dx, dy),
                OutlineShape.Plus => Manhattan(ex, ey, dx, dy),
                _ => Euclidean(ex, ey, dx, dy),
            };
        }

        /// <summary>The pixels whose centres can lie within <paramref name="reach"/> of the segment.</summary>
        public void Bounds(BoundarySegment s, float reach, int width, int height, out int firstRow, out int lastRow, out int firstColumn, out int lastColumn)
        {
            float reachY = reach / HeightScale;
            firstRow = Math.Max(0, (int)MathF.Floor(Math.Min(s.Y0, s.Y1) - reachY - 0.5f));
            lastRow = Math.Min(height - 1, (int)MathF.Ceiling(Math.Max(s.Y0, s.Y1) + reachY - 0.5f));
            firstColumn = Math.Max(0, (int)MathF.Floor(Math.Min(s.X0, s.X1) - reach - 0.5f));
            lastColumn = Math.Min(width - 1, (int)MathF.Ceiling(Math.Max(s.X0, s.X1) + reach - 0.5f));
        }

        /// <summary>Euclidean distance from (ex, ey) to the segment from the origin to (dx, dy).</summary>
        private static double Euclidean(double ex, double ey, double dx, double dy)
        {
            double lengthSquared = (dx * dx) + (dy * dy);
            double t = lengthSquared > 0 ? Math.Clamp(((ex * dx) + (ey * dy)) / lengthSquared, 0, 1) : 0;
            double vx = ex - (t * dx), vy = ey - (t * dy);
            return Math.Sqrt((vx * vx) + (vy * vy));
        }

        /// <summary>
        /// Chebyshev distance: <c>max(|ex − t·dx|, |ey − t·dy|)</c> is convex and piecewise linear
        /// in t, so its minimum over [0, 1] lies at an end, where a term vanishes, or where the
        /// two terms meet.
        /// </summary>
        private static double Chebyshev(double ex, double ey, double dx, double dy)
        {
            double best = Math.Min(Max(ex, ey), Max(ex - dx, ey - dy));
            Try(dx != 0 ? ex / dx : -1, ref best);
            Try(dy != 0 ? ey / dy : -1, ref best);
            Try(dx != dy ? (ex - ey) / (dx - dy) : -1, ref best);
            Try(dx != -dy ? (ex + ey) / (dx + dy) : -1, ref best);
            return best;

            void Try(double t, ref double result)
            {
                if (t > 0 && t < 1)
                    result = Math.Min(result, Max(ex - (t * dx), ey - (t * dy)));
            }

            static double Max(double a, double b) => Math.Max(Math.Abs(a), Math.Abs(b));
        }

        /// <summary>Manhattan distance: convex and piecewise linear in t, breaking where a term vanishes.</summary>
        private static double Manhattan(double ex, double ey, double dx, double dy)
        {
            double best = Math.Min(Math.Abs(ex) + Math.Abs(ey), Math.Abs(ex - dx) + Math.Abs(ey - dy));
            Try(dx != 0 ? ex / dx : -1, ref best);
            Try(dy != 0 ? ey / dy : -1, ref best);
            return best;

            void Try(double t, ref double result)
            {
                if (t > 0 && t < 1)
                    result = Math.Min(result, Math.Abs(ex - (t * dx)) + Math.Abs(ey - (t * dy)));
            }
        }
    }

    /// <summary>The layer cropped to its non-zero pixels, bearings moved to match.</summary>
    private static RasterizedGlyph Crop(int codepoint, int glyphIndex, byte[] coverage, int w, int h, int bearingX, int bearingY, int advance)
    {
        int left = w, right = -1, top = h, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (coverage[(y * w) + x] == 0)
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        int cw = Math.Max(0, right - left + 1), ch = Math.Max(0, bottom - top + 1);
        var bitmap = new byte[cw * ch];
        for (int y = 0; y < ch; y++)
            Array.Copy(coverage, ((top + y) * w) + left, bitmap, y * cw, cw);

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = bitmap,
            Width = cw,
            Height = ch,
            Pitch = cw,
            Metrics = new GlyphMetrics(
                BearingX: cw == 0 ? 0 : bearingX + left,
                BearingY: ch == 0 ? 0 : bearingY - top,
                Advance: advance,
                Width: cw,
                Height: ch),
            Format = PixelFormat.Grayscale8,
        };
    }
}
