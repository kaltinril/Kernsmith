using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 165 — pins down which bounds the bitmap grid comes from. Roboto places on-curve
/// points at every extremum, so its control hull and its outline bounds coincide and a
/// real-font test cannot tell the two apart; these use a glyph built to make them differ.
/// </summary>
public class GlyphBoxTests
{
    /// <summary>
    /// A triangle whose apex is a Bezier control point pulled far above the curve. The
    /// <c>glyf</c> header bounds describe where the ink actually reaches; the control hull
    /// reaches the apex, which the curve never touches.
    /// </summary>
    private static ParsedGlyph OvershootingControlPoint() =>
        new(
            glyphIndex: 1,
            contours:
            [
                new GlyphContour(
                [
                    new GlyphPoint(0, 0, OnCurve: true),
                    new GlyphPoint(100, 400, OnCurve: false),   // control, far above the ink
                    new GlyphPoint(200, 0, OnCurve: true),
                ]),
            ],
            xMin: 0,
            yMin: 0,
            xMax: 200,
            yMax: 200, // the quadratic peaks halfway to its control point
            isComposite: false);

    [Fact]
    public void Compute_UsesOutlineBounds_NotTheControlHull()
    {
        var glyph = OvershootingControlPoint();
        var outline = OutlineExtractor.Extract(glyph);

        // Guard: the hull really is taller, otherwise this test proves nothing.
        outline.YMax.ShouldBeGreaterThan(glyph.YMax);

        var box = GlyphBox.Compute(glyph, outline, scale: 0.1f);

        box.Height.ShouldBe(20, "expected the 200-unit ink height, not the 400-unit control hull");
        box.BearingY.ShouldBe(20);
        box.Width.ShouldBe(20);
        box.BearingX.ShouldBe(0);
    }

    [Fact]
    public void Compute_FallsBackToTheHull_WhenHeaderBoundsAreDegenerate()
    {
        // Some fonts leave a composite's header box zeroed. Rendering nothing is worse than
        // rendering into a box a fraction of a pixel too large.
        var source = OvershootingControlPoint();
        var broken = new ParsedGlyph(1, source.Contours, 0, 0, 0, 0, isComposite: true);
        var outline = OutlineExtractor.Extract(broken);

        var box = GlyphBox.Compute(broken, outline, scale: 0.1f);

        box.IsEmpty.ShouldBeFalse();
        box.Height.ShouldBeGreaterThan(20, "the control hull is the fallback, and it overshoots the ink");
    }

    [Fact]
    public void Compute_EmptyOutline_IsEmpty()
    {
        var box = GlyphBox.Compute(ParsedGlyph.Empty(3), GlyphOutline.Empty, scale: 0.1f);

        box.IsEmpty.ShouldBeTrue();
        box.Width.ShouldBe(0);
        box.Height.ShouldBe(0);
    }
}
