using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 164 — signed-area trapezoid coverage. The first block pins the coverage math on
/// shapes whose exact area is known by hand; the later blocks check real glyphs.
/// </summary>
public class ScanlineRasterizerTests
{
    // ---------------------------------------------------------------- analytic coverage

    [Fact]
    public void PixelAlignedRectangle_IsSolidInsideAndEmptyOutside()
    {
        // A 4x4 rectangle snapped to the pixel grid: every covered pixel is covered 100%,
        // so there is no anti-aliased fringe anywhere.
        var bitmap = Render(8, 8, Rect(2f, 2f, 6f, 6f));

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool inside = x >= 2 && x < 6 && y >= 2 && y < 6;
                bitmap[(y * 8) + x].ShouldBe((byte)(inside ? 255 : 0), $"pixel ({x},{y})");
            }
        }
    }

    [Fact]
    public void EdgeOnAPixelCentre_GivesThatColumnHalfCoverage()
    {
        // Left edge at x = 4.5 splits column 4 down the middle: coverage 0.5 => 0.5*255 = 127.5,
        // which truncates to 127. Columns 5-7 are fully inside, 0-3 fully outside.
        var bitmap = Render(8, 8, Rect(4.5f, 0f, 8f, 8f));

        for (int y = 0; y < 8; y++)
        {
            bitmap[(y * 8) + 3].ShouldBe((byte)0);
            ((int)bitmap[(y * 8) + 4]).ShouldBeInRange(125, 129, $"row {y}");
            bitmap[(y * 8) + 5].ShouldBe((byte)255);
            bitmap[(y * 8) + 7].ShouldBe((byte)255);
        }
    }

    [Fact]
    public void QuarterOfAPixel_GivesAQuarterCoverage()
    {
        // Half a pixel wide (x 4.5-5.0) by half a pixel tall (y 2.0-2.5) = 0.25 of pixel (4,2).
        // 0.25 * 255 = 63.75 => 63.
        var bitmap = Render(8, 8, Rect(4.5f, 2f, 5f, 2.5f));

        ((int)bitmap[(2 * 8) + 4]).ShouldBeInRange(62, 66);
        // Nothing spills into the neighbours.
        bitmap[(2 * 8) + 5].ShouldBe((byte)0);
        bitmap[(2 * 8) + 3].ShouldBe((byte)0);
        bitmap[(1 * 8) + 4].ShouldBe((byte)0);
        bitmap[(3 * 8) + 4].ShouldBe((byte)0);
    }

    [Fact]
    public void DiagonalTriangle_AntiAliasesTheDiagonalAndPreservesTotalArea()
    {
        // The 45 degree triangle (0,0)-(8,8)-(0,8): every pixel on the diagonal is cut exactly
        // in half, everything left of it is solid, everything right of it is empty.
        // True area = 8*8/2 = 32 pixels.
        var bitmap = Render(8, 8, Triangle());

        float totalCoverage = 0f;
        for (int i = 0; i < bitmap.Length; i++)
            totalCoverage += bitmap[i] / 255f;

        totalCoverage.ShouldBe(32f, 0.25f);

        for (int y = 0; y < 8; y++)
        {
            ((int)bitmap[(y * 8) + y]).ShouldBeInRange(125, 129, $"diagonal pixel ({y},{y})");
            if (y > 0)
                bitmap[(y * 8) + (y - 1)].ShouldBe((byte)255, $"solid pixel ({y - 1},{y})");
            if (y < 7)
                bitmap[(y * 8) + y + 1].ShouldBe((byte)0, $"empty pixel ({y + 1},{y})");
        }
    }

    [Fact]
    public void ReversedWinding_ProducesTheSameCoverage()
    {
        // The accumulator is signed, so the reversed contour accumulates -1 instead of +1;
        // taking the absolute value has to make the two indistinguishable.
        var clockwise = Render(8, 8, Rect(1.25f, 1f, 6.75f, 7f));
        var counterClockwise = Render(8, 8, Rect(1.25f, 1f, 6.75f, 7f, clockwise: false));

        counterClockwise.ShouldBe(clockwise);
        clockwise.ShouldContain((byte)255);
    }

    [Fact]
    public void ContourInsideAnOppositelyWoundContour_LeavesAHole()
    {
        // Non-zero winding: the inner contour runs the other way, so the two cancel and the
        // middle of the shape is empty - this is how 'O' gets its counter.
        var edges = Rect(0f, 0f, 8f, 8f).Concat(Rect(2f, 2f, 6f, 6f, clockwise: false)).ToArray();

        var bitmap = Render(8, 8, edges);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool inHole = x >= 2 && x < 6 && y >= 2 && y < 6;
                bitmap[(y * 8) + x].ShouldBe((byte)(inHole ? 0 : 255), $"pixel ({x},{y})");
            }
        }
    }

    [Fact]
    public void OverlappingSameWoundContours_SaturateInsteadOfWrapping()
    {
        // Winding number 2 in the overlap. Naively scaling that by 255 overflows a byte and
        // wraps to a dark band; it has to clamp to solid white instead.
        var edges = Rect(0f, 0f, 6f, 8f).Concat(Rect(2f, 0f, 8f, 8f)).ToArray();

        var bitmap = Render(8, 8, edges);

        bitmap.ShouldAllBe(v => v == 255);
    }

    [Fact]
    public void EdgesOutsideTheBitmap_AreClippedWithoutCorruptingIt()
    {
        // Well past every border in both directions. Unclipped, the column index would run
        // negative / past the buffer.
        var bitmap = Render(8, 8, Rect(-1000f, -1000f, 1000f, 1000f));

        bitmap.ShouldAllBe(v => v == 255);
    }

    [Fact]
    public void ShapesEntirelyOffTheBitmap_LeaveItEmpty()
    {
        foreach (var edges in new[]
        {
            Rect(-50f, -50f, -10f, -10f),   // above-left
            Rect(20f, 20f, 60f, 60f),       // below-right
            Rect(-50f, 2f, -10f, 6f),       // left
            Rect(20f, 2f, 60f, 6f),         // right
        })
        {
            Render(8, 8, edges).ShouldAllBe(v => v == 0);
        }
    }

    [Fact]
    public void ShapeStraddlingTheTopLeftCorner_KeepsTheVisiblePart()
    {
        // Clipping must not shift the surviving geometry: the visible quarter still lands on
        // pixels 0..3 in both axes.
        var bitmap = Render(8, 8, Rect(-20f, -20f, 4f, 4f));

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                bool inside = x < 4 && y < 4;
                bitmap[(y * 8) + x].ShouldBe((byte)(inside ? 255 : 0), $"pixel ({x},{y})");
            }
        }
    }

    [Fact]
    public void AntiAliasNone_ThresholdsAtHalfCoverage()
    {
        // Column 2 is 80% covered (=> 204, on), column 6 is 20% covered (=> 51, off).
        var bitmap = Render(8, 8, Rect(2.2f, 0f, 6.2f, 8f), AntiAliasMode.None);

        bitmap.ShouldAllBe(v => v == 0 || v == 255);
        for (int y = 0; y < 8; y++)
        {
            bitmap[(y * 8) + 2].ShouldBe((byte)255, $"row {y} column 2");
            bitmap[(y * 8) + 6].ShouldBe((byte)0, $"row {y} column 6");
        }
    }

    [Fact]
    public void AntiAliasNone_DiffersFromGrayscaleOnTheSameShape()
    {
        // Guards against the mode being ignored: the diagonal is grey in one and binary in the other.
        var grayscale = Render(8, 8, Triangle());
        var none = Render(8, 8, Triangle(), AntiAliasMode.None);

        grayscale.ShouldContain((byte)127);
        none.ShouldAllBe(v => v == 0 || v == 255);
        none.ShouldNotBe(grayscale);
    }

    [Fact]
    public void NoEdges_ProducesAnEmptyBitmapOfTheRequestedSize()
    {
        var result = ScanlineRasterizer.Rasterize([], 5, 3);

        result.Width.ShouldBe(5);
        result.Height.ShouldBe(3);
        result.Bitmap.Length.ShouldBe(15);
        result.Bitmap.ShouldAllBe(v => v == 0);
    }

    [Fact]
    public void HorizontalEdges_ContributeNothing()
    {
        // The flattener drops these, but a stray one must not skew the accumulator either.
        var edges = Rect(2f, 2f, 6f, 6f)
            .Concat([new EdgeSegment(0f, 4f, 8f, 4f), new EdgeSegment(8f, 1f, 0f, 1f)])
            .ToArray();

        Render(8, 8, edges).ShouldBe(Render(8, 8, Rect(2f, 2f, 6f, 6f)));
    }

    [Fact]
    public void DegenerateEdges_AreDiscardedInsteadOfPoisoningTheRow()
    {
        // NaN or infinite coordinates would otherwise spread through the running cover and
        // blank (or garbage) the whole scanline from that point right.
        var edges = Rect(2f, 2f, 6f, 6f)
            .Concat([
                new EdgeSegment(float.NaN, 0f, 3f, 8f),
                new EdgeSegment(1f, float.NaN, 3f, 8f),
                new EdgeSegment(float.NegativeInfinity, 0f, 3f, 8f),
                new EdgeSegment(1f, 0f, float.PositiveInfinity, 8f),
            ])
            .ToArray();

        Render(8, 8, edges).ShouldBe(Render(8, 8, Rect(2f, 2f, 6f, 6f)));
    }

    [Fact]
    public void RepeatedRasterization_DoesNotLeakStateBetweenGlyphs()
    {
        // The accumulation buffers come from ArrayPool and are not zeroed on rent, so a dirty
        // buffer from a previous (larger, busier) shape would bleed into the next result.
        var expected = Render(8, 8, Rect(2f, 2f, 6f, 6f));

        for (int i = 0; i < 25; i++)
        {
            Render(8, 8, Triangle());
            Render(64, 64, Rect(0.5f, 0.5f, 63.5f, 63.5f));
            Render(8, 8, Rect(2f, 2f, 6f, 6f)).ShouldBe(expected, $"iteration {i}");
        }
    }

    [Fact]
    public void Origin_TranslatesTheShapeIntoTheBitmap()
    {
        // Same rectangle placed at (10,10) in pixel space, with the bitmap's top-left pinned
        // to (8,8), must render identically to the untranslated one.
        var result = ScanlineRasterizer.Rasterize(Rect(10f, 10f, 14f, 14f), 8, 8, originX: 8f, originY: 8f);

        result.Bitmap.ShouldBe(Render(8, 8, Rect(2f, 2f, 6f, 6f)));
    }

    [Fact]
    public void Bearings_ArePassedThroughToTheResult()
    {
        var result = ScanlineRasterizer.Rasterize([], 4, 4, bearingX: -3, bearingY: 11);

        result.BearingX.ShouldBe(-3);
        result.BearingY.ShouldBe(11);
    }

    // ---------------------------------------------------------------- real glyphs

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(96f)]
    public void CapitalI_IsAStemOfExactlyTheOutlineWidthOnEveryRow(float pixelSize)
    {
        // Roboto's 'I' is a plain rectangle, so the exact answer is known: every row that lies
        // wholly inside the glyph must carry exactly the stem's width in coverage, no matter
        // where the sub-pixel edges fall. At 12px the stem is only 1.13px wide and no column is
        // ever fully inked, which is precisely why the assertion is on the row total.
        var (bitmap, width, height) = RenderGlyph('I', pixelSize);
        var (stemWidth, top, bottom) = GlyphBox('I', pixelSize);

        int rowsChecked = 0;
        for (int y = (int)MathF.Ceiling(top); y + 1 <= bottom; y++)
        {
            float rowCoverage = 0f;
            int firstInk = -1, lastInk = -1;
            for (int x = 0; x < width; x++)
            {
                byte value = bitmap[(y * width) + x];
                rowCoverage += value / 255f;
                if (value > 0)
                {
                    if (firstInk < 0)
                        firstInk = x;
                    lastInk = x;
                }
            }

            rowCoverage.ShouldBe(stemWidth, 0.03f, $"row {y} at {pixelSize}px");

            // Hard left/right transitions: one unbroken run, nothing outside it.
            for (int x = firstInk; x <= lastInk; x++)
                bitmap[(y * width) + x].ShouldBeGreaterThan((byte)0, $"hole in the stem at ({x},{y})");

            (lastInk - firstInk + 1).ShouldBeLessThanOrEqualTo((int)MathF.Ceiling(stemWidth) + 1, $"stem bleeds at {pixelSize}px");
            rowsChecked++;
        }

        rowsChecked.ShouldBeGreaterThan(0);

        // Ink stays inside the padded bitmap: the border ring is untouched.
        AssertBorderIsEmpty(bitmap, width, height);
    }

    [Theory]
    [InlineData(32f)]
    [InlineData(96f)]
    public void CapitalI_StemsAtLeastTwoPixelsWide_HaveAFullySaturatedColumn(float pixelSize)
    {
        // A run of >= 2px always contains a whole integer column, so a correct rasterizer has
        // to reach 255 there - anything less means coverage is being under-counted.
        var (bitmap, width, _) = RenderGlyph('I', pixelSize);
        var (stemWidth, top, bottom) = GlyphBox('I', pixelSize);
        stemWidth.ShouldBeGreaterThanOrEqualTo(2f);

        int solidColumns = 0;
        for (int x = 0; x < width; x++)
        {
            bool solid = true;
            for (int y = (int)MathF.Ceiling(top); y + 1 <= bottom && solid; y++)
                solid = bitmap[(y * width) + x] == 255;

            if (solid)
                solidColumns++;
        }

        solidColumns.ShouldBeGreaterThan(0, $"no fully-inked column at {pixelSize}px");
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(96f)]
    public void CapitalO_IsHollowInTheMiddleAndInkedOnTheSides(float pixelSize)
    {
        var (bitmap, width, height) = RenderGlyph('O', pixelSize);

        int midY = height / 2;
        int midX = width / 2;

        bitmap[(midY * width) + midX].ShouldBe((byte)0, $"counter of 'O' at {pixelSize}px");
        // ...but the ring around it is inked on that same row.
        bitmap[(midY * width) + 1].ShouldBeGreaterThan((byte)0);
        bitmap[(midY * width) + (width - 2)].ShouldBeGreaterThan((byte)0);

        AssertBorderIsEmpty(bitmap, width, height);
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(96f)]
    public void CapitalX_HasTwoSeparateStrokesNearTheTopAndOneAtTheWaist(float pixelSize)
    {
        var (bitmap, width, height) = RenderGlyph('X', pixelSize);

        // A sixth of the way down, the arms are still well apart: ink on the left, ink on the
        // right, and blank pixels between them.
        int upper = Math.Max(2, height / 6);
        var (firstInk, lastInk, gap) = InkRun(bitmap, width, upper);
        firstInk.ShouldBeGreaterThanOrEqualTo(0, $"no ink on row {upper} at {pixelSize}px");
        gap.ShouldBeGreaterThan(0, $"'X' arms are not separated at {pixelSize}px");
        firstInk.ShouldBeLessThan(width / 3);
        lastInk.ShouldBeGreaterThan(2 * width / 3);

        // At the waist the strokes have merged into a single central run.
        (firstInk, lastInk, gap) = InkRun(bitmap, width, height / 2);
        gap.ShouldBe(0, $"'X' waist is broken at {pixelSize}px");
        firstInk.ShouldBeGreaterThanOrEqualTo(width / 5, $"waist reaches too far left at {pixelSize}px");
        lastInk.ShouldBeLessThanOrEqualTo(width - (width / 5), $"waist reaches too far right at {pixelSize}px");

        AssertBorderIsEmpty(bitmap, width, height);
    }

    [Theory]
    [InlineData(8f)]
    [InlineData(200f)]
    public void ExtremeSizes_StillProduceInk(float pixelSize)
    {
        var (bitmap, width, height) = RenderGlyph('O', pixelSize);

        width.ShouldBeGreaterThan(2);
        height.ShouldBeGreaterThan(2);
        bitmap.ShouldContain(v => v > 0);
        AssertBorderIsEmpty(bitmap, width, height);
    }

    [Fact]
    public void UndersizedBitmap_ClipsTheGlyphInsteadOfOverrunning()
    {
        // A 10x10 window onto the top-left corner of a 96px 'O': most of the outline runs off
        // the right and bottom edges, and its left arc runs off the left once translated.
        var (edges, originX, originY) = GlyphEdges('O', 96f);

        var result = ScanlineRasterizer.Rasterize(edges, 10, 10, originX + 6f, originY);

        result.Bitmap.Length.ShouldBe(100);
        result.Bitmap.ShouldContain(v => v > 0);
        // The corner is outside the bowl, so it stays blank even though edges pass nearby.
        result.Bitmap[0].ShouldBe((byte)0);
    }

    // ---------------------------------------------------------------- helpers

    private static byte[] Render(int width, int height, EdgeSegment[] edges, AntiAliasMode antiAlias = AntiAliasMode.Grayscale) =>
        ScanlineRasterizer.Rasterize(edges, width, height, antiAlias: antiAlias).Bitmap;

    /// <summary>An axis-aligned rectangle, wound either way. All four sides are emitted.</summary>
    private static EdgeSegment[] Rect(float left, float top, float right, float bottom, bool clockwise = true) =>
        clockwise
            ? [
                new EdgeSegment(left, top, right, top),
                new EdgeSegment(right, top, right, bottom),
                new EdgeSegment(right, bottom, left, bottom),
                new EdgeSegment(left, bottom, left, top),
            ]
            : [
                new EdgeSegment(left, top, left, bottom),
                new EdgeSegment(left, bottom, right, bottom),
                new EdgeSegment(right, bottom, right, top),
                new EdgeSegment(right, top, left, top),
            ];

    /// <summary>The lower-left half of an 8x8 box, cut by a 45 degree diagonal.</summary>
    private static EdgeSegment[] Triangle() =>
    [
        new EdgeSegment(0f, 0f, 8f, 8f),
        new EdgeSegment(8f, 8f, 0f, 8f),
        new EdgeSegment(0f, 8f, 0f, 0f),
    ];

    /// <summary>First and last inked column on a row, and how many blank pixels sit between them.</summary>
    private static (int FirstInk, int LastInk, int Gap) InkRun(byte[] bitmap, int width, int row)
    {
        int firstInk = -1, lastInk = -1, gap = 0;
        for (int x = 0; x < width; x++)
        {
            if (bitmap[(row * width) + x] > 0)
            {
                if (firstInk < 0)
                    firstInk = x;
                lastInk = x;
            }
        }

        for (int x = firstInk; x >= 0 && x <= lastInk; x++)
        {
            if (bitmap[(row * width) + x] == 0)
                gap++;
        }

        return (firstInk, lastInk, gap);
    }

    private static void AssertBorderIsEmpty(byte[] bitmap, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            bitmap[x].ShouldBe((byte)0, $"top border pixel ({x},0)");
            bitmap[((height - 1) * width) + x].ShouldBe((byte)0, $"bottom border pixel ({x},{height - 1})");
        }

        for (int y = 0; y < height; y++)
        {
            bitmap[y * width].ShouldBe((byte)0, $"left border pixel (0,{y})");
            bitmap[(y * width) + width - 1].ShouldBe((byte)0, $"right border pixel ({width - 1},{y})");
        }
    }

    /// <summary>
    /// Flattens a real glyph and sizes a bitmap around its control-hull box with a 1px border.
    /// Returns the edges plus the pixel-space origin of that bitmap.
    /// </summary>
    private static (EdgeSegment[] Edges, float OriginX, float OriginY) GlyphEdges(char character, float pixelSize)
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(character)));
        var transform = OutlineTransform.Create(pixelSize, face.Head.UnitsPerEm, face.Hhea.Ascender);

        var edges = OutlineFlattener.Flatten(outline, transform);
        float originX = MathF.Floor(transform.ToPixelX(outline.XMin)) - 1f;
        float originY = MathF.Floor(transform.ToPixelY(outline.YMax)) - 1f;
        return (edges, originX, originY);
    }

    /// <summary>
    /// The glyph's outline box expressed in the rendered bitmap's own coordinates: how wide it
    /// is and which rows it spans.
    /// </summary>
    private static (float Width, float Top, float Bottom) GlyphBox(char character, float pixelSize)
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(character)));
        var transform = OutlineTransform.Create(pixelSize, face.Head.UnitsPerEm, face.Hhea.Ascender);

        var (_, _, originY) = GlyphEdges(character, pixelSize);
        return (
            (outline.XMax - outline.XMin) * transform.Scale,
            transform.ToPixelY(outline.YMax) - originY,
            transform.ToPixelY(outline.YMin) - originY);
    }

    private static (byte[] Bitmap, int Width, int Height) RenderGlyph(char character, float pixelSize)
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(character)));
        var transform = OutlineTransform.Create(pixelSize, face.Head.UnitsPerEm, face.Hhea.Ascender);

        var (edges, originX, originY) = GlyphEdges(character, pixelSize);
        int width = (int)MathF.Ceiling(transform.ToPixelX(outline.XMax) - originX) + 1;
        int height = (int)MathF.Ceiling(transform.ToPixelY(outline.YMin) - originY) + 1;

        var result = ScanlineRasterizer.Rasterize(edges, width, height, originX, originY);
        return (result.Bitmap, result.Width, result.Height);
    }
}
