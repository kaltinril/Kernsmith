using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 163 — scaling, the Y-axis flip, adaptive De Casteljau flattening, and edge generation.
/// </summary>
public class OutlineFlattenerTests
{
    // ---------------------------------------------------------------- scaling and Y flip

    [Fact]
    public void Transform_ScalesByPixelSizeOverUnitsPerEm()
    {
        // Roboto: 2048 units per em, ascender 1900. At 32px the scale is 32/2048 = 0.015625.
        var transform = OutlineTransform.Create(pixelSize: 32f, unitsPerEm: 2048, ascentFontUnits: 1900f);

        transform.Scale.ShouldBe(0.015625f);
        transform.AscentPixels.ShouldBe(29.6875f);
        transform.ToPixelX(376f).ShouldBe(5.875f);
        // Y flips: the top of 'I' at 1456 font units sits 6.9375px below the ascent line.
        transform.ToPixelY(1456f).ShouldBe(6.9375f);
        // The baseline (y = 0) maps to the ascent row.
        transform.ToPixelY(0f).ShouldBe(29.6875f);
    }

    [Fact]
    public void Transform_AtDoubleTheSize_DoublesEveryPixelCoordinate()
    {
        var transform = OutlineTransform.Create(pixelSize: 64f, unitsPerEm: 2048, ascentFontUnits: 1900f);

        transform.Scale.ShouldBe(0.03125f);
        transform.AscentPixels.ShouldBe(59.375f);
        transform.ToPixelX(376f).ShouldBe(11.75f);
        transform.ToPixelY(1456f).ShouldBe(13.875f);
    }

    [Fact]
    public void Transform_OfARealFace_UsesHheaAscender()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());

        var transform = OutlineTransform.Create(32f, face.Head.UnitsPerEm, face.Hhea.Ascender);

        transform.Scale.ShouldBe(32f / face.Head.UnitsPerEm);
        transform.AscentPixels.ShouldBe(face.Hhea.Ascender * transform.Scale);
    }

    // ---------------------------------------------------------------- edge generation

    [Fact]
    public void StraightContour_DropsHorizontalEdgesAndKeepsWinding()
    {
        // A 1024-unit square rendered at 32px: scale 0.03125, ascent 1024 units => 32px.
        // Its two horizontal sides carry no coverage and must not appear.
        var edges = Flatten(Square(counterClockwise: true));

        edges.Length.ShouldBe(2);
        edges[0].ShouldBe(new EdgeSegment(32f, 32f, 32f, 0f));  // right side, running upward
        edges[1].ShouldBe(new EdgeSegment(0f, 0f, 0f, 32f));    // closing left side, downward
        edges.ShouldAllBe(e => e.Y0 != e.Y1);
    }

    [Fact]
    public void ReversedContour_ProducesReversedEdges()
    {
        // Winding direction is the rasterizer's sign, so it must survive flattening verbatim.
        var edges = Flatten(Square(counterClockwise: false));

        edges.Length.ShouldBe(2);
        edges[0].ShouldBe(new EdgeSegment(0f, 32f, 0f, 0f));
        edges[1].ShouldBe(new EdgeSegment(32f, 0f, 32f, 32f));
    }

    [Fact]
    public void EmptyOutline_ProducesNoEdges()
    {
        var edges = OutlineFlattener.Flatten(GlyphOutline.Empty, OutlineTransform.Create(32f, 1024, 1024f));

        edges.ShouldBeEmpty();
    }

    [Fact]
    public void RealGlyph_ProducesAClosedChainOfEdges()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex('O')));
        var transform = OutlineTransform.Create(48f, face.Head.UnitsPerEm, face.Hhea.Ascender);

        var edges = OutlineFlattener.Flatten(outline, transform);

        // Curves must have been subdivided well past the 20 straight segments of the commands.
        edges.Length.ShouldBeGreaterThan(40);
        edges.ShouldAllBe(e => e.Y0 != e.Y1);

        // Every edge's total displacement cancels out, because both contours are closed and
        // only horizontal (zero net Y) pieces were removed.
        float netY = 0f;
        foreach (var edge in edges)
            netY += edge.Y1 - edge.Y0;
        netY.ShouldBe(0f, 0.01f);
    }

    // ---------------------------------------------------------------- curve flattening

    [Fact]
    public void FlattenedCurve_StaysWithinToleranceOfTheTrueCubic()
    {
        // A single quadratic arc, rendered 1:1 (scale 1) so pixel units are font units.
        var glyph = Glyph([On(0, 0), Off(400, 500), On(0, 1000)]);
        var outline = OutlineExtractor.Extract(glyph);
        var transform = OutlineTransform.Create(pixelSize: 1024f, unitsPerEm: 1024, ascentFontUnits: 1000f);

        var edges = OutlineFlattener.Flatten(outline, transform);

        // Last edge is the straight closing segment; the rest approximate the curve.
        edges.Length.ShouldBeGreaterThan(8);
        var cubic = outline.Commands[1];
        float maxDeviation = MaxCurveDeviation(cubic, transform, edges.AsSpan(0, edges.Length - 1));
        maxDeviation.ShouldBeLessThanOrEqualTo(OutlineFlattener.DefaultTolerance);
    }

    [Fact]
    public void FlatteningTolerance_TradesEdgeCountForAccuracy()
    {
        var outline = OutlineExtractor.Extract(Glyph([On(0, 0), Off(400, 500), On(0, 1000)]));
        var transform = OutlineTransform.Create(1024f, 1024, 1000f);

        var coarse = OutlineFlattener.Flatten(outline, transform, tolerance: 4f);
        var fine = OutlineFlattener.Flatten(outline, transform, tolerance: 0.01f);

        coarse.Length.ShouldBeLessThan(fine.Length);
        var cubic = outline.Commands[1];
        float coarseDeviation = MaxCurveDeviation(cubic, transform, coarse.AsSpan(0, coarse.Length - 1));
        coarseDeviation.ShouldBeLessThanOrEqualTo(4f);
        // The measurement is not vacuous: a 4px budget really is sloppier than the default.
        coarseDeviation.ShouldBeGreaterThan(OutlineFlattener.DefaultTolerance);
        MaxCurveDeviation(cubic, transform, fine.AsSpan(0, fine.Length - 1)).ShouldBeLessThanOrEqualTo(0.01f);
    }

    [Fact]
    public void UnreachableTolerance_StopsAtTheRecursionDepthCap()
    {
        // A tolerance nine orders of magnitude below the curve's own size can never be met:
        // subdivision quarters the error per level and would need well past 16 of them.
        // The depth cap must stop it there — 2^16 curve segments plus the closing edge —
        // instead of subdividing until the output (or the stack) blows up.
        var outline = OutlineExtractor.Extract(Glyph([On(0, 0), Off(400, 500), On(0, 1000)]));
        var transform = OutlineTransform.Create(1024f, 1024, 1000f);

        var edges = OutlineFlattener.Flatten(outline, transform, tolerance: 1e-9f);

        // Without the cap this same curve subdivides to over 50 million segments.
        int capped = 1 << OutlineFlattener.MaxSubdivisionDepth;
        edges.Length.ShouldBeLessThanOrEqualTo(capped + 1);
        // ...and it really did recurse all the way down, rather than stopping early.
        edges.Length.ShouldBeGreaterThan(capped - 1000);
    }

    // ---------------------------------------------------------------- helpers

    private static EdgeSegment[] Flatten(ParsedGlyph glyph) =>
        OutlineFlattener.Flatten(
            OutlineExtractor.Extract(glyph),
            OutlineTransform.Create(pixelSize: 32f, unitsPerEm: 1024, ascentFontUnits: 1024f));

    /// <summary>A 1024x1024 square anchored at the origin, wound either way.</summary>
    private static ParsedGlyph Square(bool counterClockwise) => counterClockwise
        ? Glyph([On(0, 0), On(1024, 0), On(1024, 1024), On(0, 1024)])
        : Glyph([On(0, 0), On(0, 1024), On(1024, 1024), On(1024, 0)]);

    /// <summary>
    /// Largest distance from a densely sampled point on the (transformed) cubic to the
    /// polyline the flattener produced for it.
    /// </summary>
    private static float MaxCurveDeviation(OutlineCommand cubic, OutlineTransform transform, ReadOnlySpan<EdgeSegment> edges)
    {
        cubic.Type.ShouldBe(OutlineCommandType.CubicTo);

        // The transform is affine, so transforming the control points and evaluating is the
        // same curve as evaluating and transforming.
        float x0 = edges[0].X0, y0 = edges[0].Y0;
        float x1 = transform.ToPixelX(cubic.Ctrl1X), y1 = transform.ToPixelY(cubic.Ctrl1Y);
        float x2 = transform.ToPixelX(cubic.Ctrl2X), y2 = transform.ToPixelY(cubic.Ctrl2Y);
        float x3 = transform.ToPixelX(cubic.EndX), y3 = transform.ToPixelY(cubic.EndY);

        float worst = 0f;
        const int Samples = 4000;
        for (int i = 0; i <= Samples; i++)
        {
            float t = i / (float)Samples;
            float u = 1f - t;
            float bx = (u * u * u * x0) + (3f * u * u * t * x1) + (3f * u * t * t * x2) + (t * t * t * x3);
            float by = (u * u * u * y0) + (3f * u * u * t * y1) + (3f * u * t * t * y2) + (t * t * t * y3);

            float nearest = float.MaxValue;
            foreach (var edge in edges)
                nearest = Math.Min(nearest, DistanceToSegment(bx, by, edge));

            worst = Math.Max(worst, nearest);
        }

        return worst;
    }

    private static float DistanceToSegment(float px, float py, EdgeSegment edge)
    {
        float dx = edge.X1 - edge.X0;
        float dy = edge.Y1 - edge.Y0;
        float lengthSquared = (dx * dx) + (dy * dy);

        float t = lengthSquared <= 0f
            ? 0f
            : Math.Clamp((((px - edge.X0) * dx) + ((py - edge.Y0) * dy)) / lengthSquared, 0f, 1f);

        float cx = edge.X0 + (t * dx);
        float cy = edge.Y0 + (t * dy);
        return MathF.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }

    private static GlyphPoint On(float x, float y) => new(x, y, OnCurve: true);

    private static GlyphPoint Off(float x, float y) => new(x, y, OnCurve: false);

    private static ParsedGlyph Glyph(params GlyphPoint[][] contours)
    {
        var built = new GlyphContour[contours.Length];
        for (int i = 0; i < contours.Length; i++)
            built[i] = new GlyphContour(contours[i]);

        return new ParsedGlyph(0, built, 0, 0, 0, 0, isComposite: false);
    }
}
