using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Synthetic outlines with known geometry, in font units that map 1:1 onto pixels (scale 1,
/// baseline on row 0, so pixel y = -font y). Contours follow TrueType's convention — outer
/// contours clockwise in font units (Y up) — unless a test reverses them on purpose.
/// </summary>
internal static class TestOutlines
{
    /// <summary>The 1:1 font-unit to pixel mapping every synthetic glyph uses.</summary>
    public static readonly OutlineTransform Identity = new(1f, 0f);

    /// <summary>An axis-aligned rectangle contour, clockwise in font units unless reversed.</summary>
    public static OutlineCommand[] Rect(float x0, float y0, float x1, float y1, bool reversed = false) =>
        reversed
            ? [OutlineCommand.Move(x0, y0), OutlineCommand.Line(x1, y0), OutlineCommand.Line(x1, y1), OutlineCommand.Line(x0, y1), OutlineCommand.CloseAt(x0, y0)]
            : [OutlineCommand.Move(x0, y0), OutlineCommand.Line(x0, y1), OutlineCommand.Line(x1, y1), OutlineCommand.Line(x1, y0), OutlineCommand.CloseAt(x0, y0)];

    /// <summary>A closed polygon through the given (x, y) font-unit vertices, in the order given.</summary>
    public static OutlineCommand[] Polygon(params (float X, float Y)[] points)
    {
        var commands = new List<OutlineCommand> { OutlineCommand.Move(points[0].X, points[0].Y) };
        for (int i = 1; i < points.Length; i++)
            commands.Add(OutlineCommand.Line(points[i].X, points[i].Y));
        commands.Add(OutlineCommand.CloseAt(points[0].X, points[0].Y));
        return [.. commands];
    }

    /// <summary>
    /// A circle as four cubic quarter-arcs (control distance 0.5523·r), clockwise in font
    /// units. The cubic approximation deviates from the true circle by at most 0.027% of r.
    /// </summary>
    public static OutlineCommand[] Circle(float cx, float cy, float r)
    {
        float k = 0.5522847498f * r;
        return
        [
            OutlineCommand.Move(cx - r, cy),
            OutlineCommand.Cubic(cx - r, cy + k, cx - k, cy + r, cx, cy + r),
            OutlineCommand.Cubic(cx + k, cy + r, cx + r, cy + k, cx + r, cy),
            OutlineCommand.Cubic(cx + r, cy - k, cx + k, cy - r, cx, cy - r),
            OutlineCommand.Cubic(cx - k, cy - r, cx - r, cy - k, cx - r, cy),
            OutlineCommand.CloseAt(cx - r, cy),
        ];
    }

    /// <summary>Concatenates contours into one outline.</summary>
    public static GlyphOutline Outline(params OutlineCommand[][] contours) =>
        GlyphOutline.FromCommands([.. contours.SelectMany(c => c)]);

    /// <summary>Stages 3-4 for a synthetic outline: tight box at scale 1, baseline on row 0.</summary>
    public static PreparedGlyph Prepare(GlyphOutline outline, int advance = 0) =>
        new(outline, Identity, GlyphBox.Compute(outline, null, 1f), advance);

    /// <summary>Signed distance (positive inside) from a point to an axis-aligned pixel-space box.</summary>
    public static double BoxDistance(double px, double py, double left, double top, double right, double bottom)
    {
        double dx = Math.Max(left - px, px - right);
        double dy = Math.Max(top - py, py - bottom);
        double outside = Math.Sqrt((Math.Max(dx, 0) * Math.Max(dx, 0)) + (Math.Max(dy, 0) * Math.Max(dy, 0)));
        double inside = Math.Min(Math.Max(dx, dy), 0);
        return -(outside + inside);
    }

    /// <summary>
    /// FreeType's 8-bit SDF encoding, as measured from its output: 128 ± ⌊|d|·128/spread⌋,
    /// inside bright, clamped to 0..255.
    /// </summary>
    public static int EncodeLikeFreeType(double distance, double spread)
    {
        int magnitude = (int)Math.Floor(Math.Abs(distance) * 128 / spread);
        return distance >= 0 ? Math.Min(255, 128 + magnitude) : Math.Max(0, 128 - magnitude);
    }
}
