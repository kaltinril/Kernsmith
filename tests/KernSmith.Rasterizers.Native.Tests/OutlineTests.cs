using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 163 — outline extraction: turning parsed <c>glyf</c> contours into a normalized
/// MoveTo/LineTo/CubicTo/Close command list.
/// </summary>
public class OutlineExtractorTests
{
    private static NativeFontFace LoadRoboto() => NativeFontFace.Load(TestFonts.RobotoRegularBytes());

    private static GlyphOutline ExtractRoboto(char ch)
    {
        var face = LoadRoboto();
        return OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(ch)));
    }

    // ---------------------------------------------------------------- real glyphs

    [Fact]
    public void CapitalI_IsOneContourOfStraightLines()
    {
        // Roboto's 'I' is a plain 4-point rectangle: MoveTo + 3 LineTo + Close.
        var outline = ExtractRoboto('I');

        outline.IsEmpty.ShouldBeFalse();
        outline.Commands.Length.ShouldBe(5);
        Count(outline, OutlineCommandType.MoveTo).ShouldBe(1);
        Count(outline, OutlineCommandType.LineTo).ShouldBe(3);
        Count(outline, OutlineCommandType.Close).ShouldBe(1);
        Count(outline, OutlineCommandType.CubicTo).ShouldBe(0);

        outline.Commands[0].ShouldBe(OutlineCommand.Move(376, 1456));
        // Close carries the contour's start point so the closing edge can be generated.
        outline.Commands[4].ShouldBe(OutlineCommand.CloseAt(376, 1456));
    }

    [Fact]
    public void CapitalO_HasTwoContoursAndCurves()
    {
        // Two contours (outer ring + counter), each 18 points with 8 off-curve controls.
        var outline = ExtractRoboto('O');

        Count(outline, OutlineCommandType.MoveTo).ShouldBe(2);
        Count(outline, OutlineCommandType.Close).ShouldBe(2);
        Count(outline, OutlineCommandType.CubicTo).ShouldBe(16);
        Count(outline, OutlineCommandType.LineTo).ShouldBe(2);
        outline.Commands.Length.ShouldBe(22);
    }

    [Fact]
    public void CapitalA_IsPolygonalWithNoCurves()
    {
        // Roboto draws 'A' as straight lines only: an 8-point outer contour and a
        // 3-point counter. (The phase plan calls 'A' "mixed"; this font's is not.)
        var outline = ExtractRoboto('A');

        Count(outline, OutlineCommandType.MoveTo).ShouldBe(2);
        Count(outline, OutlineCommandType.Close).ShouldBe(2);
        Count(outline, OutlineCommandType.CubicTo).ShouldBe(0);
        Count(outline, OutlineCommandType.LineTo).ShouldBe(9);
        outline.Commands.Length.ShouldBe(13);
    }

    [Fact]
    public void CapitalB_MixesLinesAndCurves()
    {
        // Three contours with 15 off-curve controls in total, so 15 cubics.
        var outline = ExtractRoboto('B');

        Count(outline, OutlineCommandType.MoveTo).ShouldBe(3);
        Count(outline, OutlineCommandType.Close).ShouldBe(3);
        Count(outline, OutlineCommandType.CubicTo).ShouldBe(15);
        Count(outline, OutlineCommandType.LineTo).ShouldBe(8);
        outline.Commands.Length.ShouldBe(29);
    }

    [Fact]
    public void EveryContourStartsWithMoveToAndEndsWithClose()
    {
        var outline = ExtractRoboto('B');

        outline.Commands[0].Type.ShouldBe(OutlineCommandType.MoveTo);
        outline.Commands[^1].Type.ShouldBe(OutlineCommandType.Close);

        // Each Close must name the point its MoveTo opened at.
        float startX = 0, startY = 0;
        foreach (var command in outline.Commands)
        {
            if (command.Type == OutlineCommandType.MoveTo)
                (startX, startY) = (command.EndX, command.EndY);
            else if (command.Type == OutlineCommandType.Close)
                (command.EndX, command.EndY).ShouldBe((startX, startY));
        }
    }

    [Fact]
    public void EmptyGlyph_ProducesEmptyOutline()
    {
        var outline = ExtractRoboto(' ');

        outline.IsEmpty.ShouldBeTrue();
        outline.Commands.ShouldBeEmpty();
        outline.XMin.ShouldBe(0);
        outline.YMin.ShouldBe(0);
        outline.XMax.ShouldBe(0);
        outline.YMax.ShouldBe(0);
    }

    // ---------------------------------------------------------------- synthetic contours

    [Fact]
    public void Quadratic_IsElevatedToCubicExactly()
    {
        // Quadratic P0(0,0) C(90,180) P2(180,0):
        //   C1 = P0 + 2/3 (C - P0) = (60, 120)
        //   C2 = P2 + 2/3 (C - P2) = (120, 120)
        var outline = OutlineExtractor.Extract(Glyph([On(0, 0), Off(90, 180), On(180, 0)]));

        outline.Commands.Length.ShouldBe(3);
        outline.Commands[0].ShouldBe(OutlineCommand.Move(0, 0));

        var cubic = outline.Commands[1];
        cubic.Type.ShouldBe(OutlineCommandType.CubicTo);
        cubic.Ctrl1X.ShouldBe(60f);
        cubic.Ctrl1Y.ShouldBe(120f);
        cubic.Ctrl2X.ShouldBe(120f);
        cubic.Ctrl2Y.ShouldBe(120f);
        cubic.EndX.ShouldBe(180f);
        cubic.EndY.ShouldBe(0f);

        outline.Commands[2].ShouldBe(OutlineCommand.CloseAt(0, 0));
    }

    [Fact]
    public void ConsecutiveOffCurvePoints_GetAnImplicitOnCurveMidpoint()
    {
        // On(0,0) Off(60,120) Off(180,120) On(240,0): the implicit on-curve point sits at
        // the midpoint of the two controls, (120,120), splitting this into two quadratics.
        var outline = OutlineExtractor.Extract(
            Glyph([On(0, 0), Off(60, 120), Off(180, 120), On(240, 0)]));

        outline.Commands.Length.ShouldBe(4);
        outline.Commands[1].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[1].EndX.ShouldBe(120f);
        outline.Commands[1].EndY.ShouldBe(120f);
        outline.Commands[1].Ctrl1X.ShouldBe(40f);   // (0 + 2*60)/3
        outline.Commands[1].Ctrl1Y.ShouldBe(80f);   // (0 + 2*120)/3
        outline.Commands[1].Ctrl2X.ShouldBe(80f);   // (120 + 2*60)/3
        outline.Commands[1].Ctrl2Y.ShouldBe(120f);  // (120 + 2*120)/3

        outline.Commands[2].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[2].EndX.ShouldBe(240f);
        outline.Commands[2].EndY.ShouldBe(0f);
        outline.Commands[3].ShouldBe(OutlineCommand.CloseAt(0, 0));
    }

    [Fact]
    public void ContourStartingOffCurve_StartsAtTheLastOnCurvePoint()
    {
        // TrueType allows a contour to begin on a control point. With an on-curve point at
        // the end of the list, that point is the real start and is not walked twice.
        var outline = OutlineExtractor.Extract(
            Glyph([Off(0, 100), On(100, 100), On(100, 0), On(0, 0)]));

        outline.Commands[0].ShouldBe(OutlineCommand.Move(0, 0));
        outline.Commands.Length.ShouldBe(4);
        outline.Commands[1].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[1].EndX.ShouldBe(100f);
        outline.Commands[1].EndY.ShouldBe(100f);
        outline.Commands[2].ShouldBe(OutlineCommand.Line(100, 0));
        outline.Commands[3].ShouldBe(OutlineCommand.CloseAt(0, 0));
    }

    [Fact]
    public void ContourWithNoOnCurvePointAtEither_End_StartsAtTheSynthesizedMidpoint()
    {
        // First and last points are both off-curve, so the contour's start is the implicit
        // on-curve midpoint between them: ((0+100)/2, (100+100)/2) = (50, 100).
        var outline = OutlineExtractor.Extract(
            Glyph([Off(0, 100), On(50, 0), Off(100, 100)]));

        outline.Commands[0].ShouldBe(OutlineCommand.Move(50, 100));
        outline.Commands.Length.ShouldBe(4);
        outline.Commands[1].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[1].EndX.ShouldBe(50f);
        outline.Commands[1].EndY.ShouldBe(0f);
        // The final curve runs from the last on-curve point back to the start.
        outline.Commands[2].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[2].EndX.ShouldBe(50f);
        outline.Commands[2].EndY.ShouldBe(100f);
        outline.Commands[3].ShouldBe(OutlineCommand.CloseAt(50, 100));
    }

    [Fact]
    public void ContourEndingOffCurve_CurvesBackToTheStart()
    {
        var outline = OutlineExtractor.Extract(
            Glyph([On(0, 0), On(180, 0), Off(90, 180)]));

        outline.Commands.Length.ShouldBe(4);
        outline.Commands[0].ShouldBe(OutlineCommand.Move(0, 0));
        outline.Commands[1].ShouldBe(OutlineCommand.Line(180, 0));
        outline.Commands[2].Type.ShouldBe(OutlineCommandType.CubicTo);
        outline.Commands[2].EndX.ShouldBe(0f);
        outline.Commands[2].EndY.ShouldBe(0f);
        outline.Commands[3].ShouldBe(OutlineCommand.CloseAt(0, 0));
    }

    [Fact]
    public void DegenerateContours_AreSkipped()
    {
        // A one-point contour encloses no area and cannot form an edge.
        var outline = OutlineExtractor.Extract(Glyph([On(10, 10)]));

        outline.Commands.ShouldBeEmpty();
        outline.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void BoundingBox_CoversEndpointsAndControlPoints()
    {
        // The control point at y=180 is outside the endpoints' box, and the box is
        // deliberately conservative (control-hull based) so nothing can be clipped.
        var outline = OutlineExtractor.Extract(Glyph([On(0, 0), Off(90, 180), On(180, 0)]));

        outline.XMin.ShouldBe(0f);
        outline.YMin.ShouldBe(0f);
        outline.XMax.ShouldBe(180f);
        outline.YMax.ShouldBe(120f); // the elevated cubic's control points cap out at 120
    }

    [Fact]
    public void BoundingBoxOfRealGlyph_FitsInsideTheDeclaredGlyfBox()
    {
        var face = LoadRoboto();
        var glyph = face.GetGlyph(face.GetGlyphIndex('O'));

        var outline = OutlineExtractor.Extract(glyph);

        outline.XMin.ShouldBeGreaterThanOrEqualTo(glyph.XMin);
        outline.YMin.ShouldBeGreaterThanOrEqualTo(glyph.YMin);
        outline.XMax.ShouldBeLessThanOrEqualTo(glyph.XMax);
        outline.YMax.ShouldBeLessThanOrEqualTo(glyph.YMax);
    }

    // ---------------------------------------------------------------- helpers

    private static int Count(GlyphOutline outline, OutlineCommandType type)
    {
        int count = 0;
        foreach (var command in outline.Commands)
        {
            if (command.Type == type)
                count++;
        }

        return count;
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
