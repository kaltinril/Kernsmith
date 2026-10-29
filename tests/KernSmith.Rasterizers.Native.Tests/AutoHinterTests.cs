using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 174 — the auto-hinter's building blocks in isolation: blue-zone measurement from a
/// font's reference glyphs, and pixel-space grid fitting of synthetic outlines.
/// </summary>
public class AutoHinterTests
{
    // ---- Blue zones -------------------------------------------------------------------------

    [Fact]
    public void BlueZones_OnRoboto_MatchTheDeclaredOs2Heights()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var metrics = MeasureRoboto(face);

        metrics.ShouldNotBeNull();
        face.Os2.XHeight.ShouldBeGreaterThan((short)0, "fixture must declare sxHeight for this test to mean anything");
        face.Os2.CapHeight.ShouldBeGreaterThan((short)0, "fixture must declare sCapHeight for this test to mean anything");

        // Measured from 'x' and 'H', not copied from OS/2 — within 1% of the em of what the
        // font declares.
        float tolerance = face.Head.UnitsPerEm * 0.01f;
        var xHeight = metrics!.Find(BlueZoneKind.XHeight);
        var capHeight = metrics.Find(BlueZoneKind.CapHeight);
        xHeight.ShouldNotBeNull();
        capHeight.ShouldNotBeNull();
        xHeight!.Value.Flat.ShouldBe(face.Os2.XHeight, tolerance);
        capHeight!.Value.Flat.ShouldBe(face.Os2.CapHeight, tolerance);

        // Round glyphs overshoot the flat heights, upward at the top and downward at the baseline.
        xHeight.Value.Overshoot.ShouldBeGreaterThan(xHeight.Value.Flat);
        capHeight.Value.Overshoot.ShouldBeGreaterThan(capHeight.Value.Flat);
        var baseline = metrics.Find(BlueZoneKind.Baseline);
        baseline.ShouldNotBeNull();
        baseline!.Value.Flat.ShouldBe(0f, tolerance);
        baseline.Value.Overshoot.ShouldBeLessThan(0f);

        var descender = metrics.Find(BlueZoneKind.Descender);
        descender.ShouldNotBeNull();
        descender!.Value.Flat.ShouldBeLessThan(-face.Head.UnitsPerEm * 0.1f);

        metrics.StandardHorizontalStem.ShouldBeGreaterThan(0f);
        metrics.StandardHorizontalStem.ShouldBeLessThan(face.Head.UnitsPerEm * 0.15f);
    }

    [Fact]
    public void BlueZones_AreNotMeasured_WhenTheFontHasNoLatinReferencesAndNoOs2Heights()
    {
        // A non-Latin font: no 'x'/'H' reference glyphs and no OS/2 heights. Hinting must be
        // skipped rather than guessed.
        var noHeights = new Os2Table(Version: 1, TypoAscender: 800, TypoDescender: -200, WinAscent: 800, WinDescent: 200, XHeight: 0, CapHeight: 0);

        FontHintMetrics.Measure(_ => null, noHeights, unitsPerEm: 1000).ShouldBeNull();
    }

    [Fact]
    public void BlueZones_FallBackToOs2Heights_WhenTheReferenceGlyphsAreMissing()
    {
        var os2 = new Os2Table(Version: 2, TypoAscender: 800, TypoDescender: -200, WinAscent: 800, WinDescent: 200, XHeight: 500, CapHeight: 700);

        var metrics = FontHintMetrics.Measure(_ => null, os2, unitsPerEm: 1000);

        metrics.ShouldNotBeNull();
        metrics!.Find(BlueZoneKind.XHeight)!.Value.Flat.ShouldBe(500f);
        metrics.Find(BlueZoneKind.CapHeight)!.Value.Flat.ShouldBe(700f);
    }

    // ---- Grid fitting -----------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fit_RoundsAFractionalStem_ToAWholePixelOnTheGrid(bool counterClockwise)
    {
        // A 1.4px-thick horizontal bar sitting at a fractional height. Clockwise is TrueType's
        // outer-contour winding, counter-clockwise CFF's; the fitter must handle both.
        var bar = Rectangle(0f, 2.3f, 10f, 3.7f, counterClockwise);

        var fitted = GridFitter.Fit(bar, ScaledHintMetrics.WithoutZones(maxStem: 4f));

        fitted.ShouldNotBeNull();
        var (bottom, top) = VerticalExtent(fitted!);
        (top - bottom).ShouldBe(1f, 1e-4f);
        bottom.ShouldBe(MathF.Round(bottom), 1e-4f);
        // The stem stays where it was: its centre (3.0) moves by at most half a pixel.
        ((top + bottom) / 2f).ShouldBe(3.0f, 0.5f);
    }

    [Fact]
    public void Fit_KeepsAHairlineStem_AtLeastOnePixelThick()
    {
        var hairline = Rectangle(0f, 5.1f, 10f, 5.5f, counterClockwise: false);

        var fitted = GridFitter.Fit(hairline, ScaledHintMetrics.WithoutZones(maxStem: 4f));

        fitted.ShouldNotBeNull();
        var (bottom, top) = VerticalExtent(fitted!);
        (top - bottom).ShouldBe(1f, 1e-4f);
        bottom.ShouldBe(MathF.Round(bottom), 1e-4f);
    }

    [Fact]
    public void Fit_SnapsEdgesInABlueZone_ToTheZonesPixelLine()
    {
        // A tall box whose bottom sits 0.3px above the baseline and whose top is 0.4px below a
        // flat x-height of 6.6px: both edges snap to their zone (baseline 0, x-height 7) and
        // neither is a stem, since 6+ pixels apart exceeds the maximum stem.
        var box = Rectangle(0f, 0.3f, 4f, 6.2f, counterClockwise: false);
        var metrics = new ScaledHintMetrics(
            [
                new PixelZone(IsTop: false, Flat: 0f, Overshoot: -0.1f, SnappedFlat: 0f, SnappedOvershoot: 0f),
                new PixelZone(IsTop: true, Flat: 6.6f, Overshoot: 6.75f, SnappedFlat: 7f, SnappedOvershoot: 7f),
            ],
            StandardStem: 0f,
            ZoneTolerance: 0.5f,
            MaxStem: 2f);

        var fitted = GridFitter.Fit(box, metrics);

        fitted.ShouldNotBeNull();
        var (bottom, top) = VerticalExtent(fitted!);
        bottom.ShouldBe(0f, 1e-4f);
        top.ShouldBe(7f, 1e-4f);
    }

    [Fact]
    public void Fit_InterpolatesUntouchedPoints_BetweenHintedEdges()
    {
        // A trapezoid-ish contour: flat bottom 0.3 -> 0, flat top 6.2 -> 7 (zones as above);
        // the point at y=3.25 on the slanted right side sits halfway and must stay halfway.
        var shape = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0f, 0.3f),
            OutlineCommand.Line(0f, 6.2f),
            OutlineCommand.Line(4f, 6.2f),
            OutlineCommand.Line(5f, 3.25f),
            OutlineCommand.Line(4f, 0.3f),
            OutlineCommand.CloseAt(0f, 0.3f),
        ]);
        var metrics = new ScaledHintMetrics(
            [
                new PixelZone(IsTop: false, Flat: 0f, Overshoot: -0.1f, SnappedFlat: 0f, SnappedOvershoot: 0f),
                new PixelZone(IsTop: true, Flat: 6.6f, Overshoot: 6.75f, SnappedFlat: 7f, SnappedOvershoot: 7f),
            ],
            StandardStem: 0f,
            ZoneTolerance: 0.5f,
            MaxStem: 2f);

        var fitted = GridFitter.Fit(shape, metrics);

        fitted.ShouldNotBeNull();
        var middle = fitted!.Commands[3];
        middle.EndX.ShouldBe(5f);
        middle.EndY.ShouldBe(3.5f, 1e-4f);
    }

    [Fact]
    public void Fit_ReturnsNull_WhenThereIsNothingToHint()
    {
        // A diamond has no horizontal edges at all.
        var diamond = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0f, 2f),
            OutlineCommand.Line(2f, 4f),
            OutlineCommand.Line(4f, 2f),
            OutlineCommand.Line(2f, 0f),
            OutlineCommand.CloseAt(0f, 2f),
        ]);

        GridFitter.Fit(diamond, ScaledHintMetrics.WithoutZones(maxStem: 4f)).ShouldBeNull();
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static FontHintMetrics? MeasureRoboto(NativeFontFace face)
    {
        var decoder = OutlineDecoders.ForFace(face)!;
        var options = new RasterOptions { Size = 16 };
        return FontHintMetrics.Measure(
            codepoint =>
            {
                int glyph = face.GetGlyphIndex(codepoint);
                return glyph == 0 ? null : decoder.Decode(glyph, options).Outline;
            },
            face.Os2,
            face.Head.UnitsPerEm);
    }

    /// <summary>An axis-aligned rectangle, wound clockwise (TrueType) or counter-clockwise (CFF), Y up.</summary>
    private static GlyphOutline Rectangle(float left, float bottom, float right, float top, bool counterClockwise) =>
        GlyphOutline.FromCommands(counterClockwise
            ?
            [
                OutlineCommand.Move(left, bottom),
                OutlineCommand.Line(right, bottom),
                OutlineCommand.Line(right, top),
                OutlineCommand.Line(left, top),
                OutlineCommand.CloseAt(left, bottom),
            ]
            :
            [
                OutlineCommand.Move(left, bottom),
                OutlineCommand.Line(left, top),
                OutlineCommand.Line(right, top),
                OutlineCommand.Line(right, bottom),
                OutlineCommand.CloseAt(left, bottom),
            ]);

    private static (float Bottom, float Top) VerticalExtent(GlyphOutline outline)
    {
        var bounds = OutlineBounds.Tight(outline);
        return (bounds.YMin, bounds.YMax);
    }
}
