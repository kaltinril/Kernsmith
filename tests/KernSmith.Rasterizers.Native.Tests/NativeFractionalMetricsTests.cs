using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// With <see cref="RasterOptions.FractionalMetrics"/>, Native reports each glyph's unrounded
/// advance (the font's <c>hmtx</c> advance at the size, after any width or bold transform) and
/// left side bearing, and under hinting how far the fitter moved the ink; the rounded
/// <see cref="RasterizedGlyph.Metrics"/> are unchanged.
/// </summary>
public class NativeFractionalMetricsTests
{
    /// <summary>Roboto's em size for a BMFont cell, 2048 / (2146 + 555) per pixel of cell.</summary>
    private static float RobotoCell(float cell) => cell * 2048f / (2146f + 555f);

    private static RasterOptions Options(HintLevel level, bool fractional = true) =>
        new() { Size = RobotoCell(12), HintLevel = level, EnableHinting = level != HintLevel.None, FractionalMetrics = fractional };

    [Fact]
    public void Off_ByDefault_ReportsNothing()
    {
        using var native = Load();
        native.RasterizeGlyph('n', new RasterOptions { Size = RobotoCell(12) })!.Fractional.ShouldBeNull();
    }

    [Theory]
    [InlineData(HintLevel.None)]
    [InlineData(HintLevel.Normal)]
    public void Advance_IsTheFontsAdvanceAtTheSize_AndRoundsToTheWrittenOne(HintLevel level)
    {
        using var native = Load();
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var options = Options(level);
        float scale = options.Size / face.Head.UnitsPerEm;

        foreach (char c in "nHiMo. ")
        {
            var glyph = native.RasterizeGlyph(c, options).ShouldNotBeNull();
            var f = glyph.Fractional.ShouldNotBeNull(c.ToString());
            int index = face.GetGlyphIndex(c);
            f.Advance.ShouldBe(face.Hmtx.GetAdvanceWidth(index) * scale, 1e-3f, c.ToString());
            f.BearingX.ShouldBe(face.Hmtx.GetLeftSideBearing(index) * scale, 1e-3f, $"lsb of {c}");
            Math.Round(f.Advance, MidpointRounding.AwayFromZero).ShouldBe(glyph.Metrics.Advance, c.ToString());
        }
    }

    [Fact]
    public void Unhinted_HasNoDeltas_Normal_ReportsHowFarTheInkMoved()
    {
        using var native = Load();

        foreach (char c in "nHio")
        {
            var unhinted = native.RasterizeGlyph(c, Options(HintLevel.None))!.Fractional!.Value;
            unhinted.AdvanceDelta.ShouldBeNull(c.ToString());
            unhinted.BearingXDelta.ShouldBeNull(c.ToString());

            var hinted = native.RasterizeGlyph(c, Options(HintLevel.Normal))!.Fractional!.Value;
            // Exact here; core snaps the metadata to 1/64 px.
            var delta = hinted.BearingXDelta.ShouldNotBeNull($"Normal fits {c} in x at 12 px");
            Math.Abs(delta).ShouldBeLessThanOrEqualTo(1.5f, $"the fitter moves {c} by at most a pixel and the bearing slack");
            hinted.Advance.ShouldBe(unhinted.Advance, "the font's advance does not depend on hinting");
            hinted.BearingX.ShouldBe(unhinted.BearingX, "nor does the font's bearing");
        }

        // The hinter keeps the font's advance (rounded once), so the advance delta is only ever
        // the stem-pair padding column, and null without it: either way it closes to the advance written.
        var n = native.RasterizeGlyph('n', Options(HintLevel.Normal))!;
        var f = n.Fractional!.Value;
        (Math.Round(f.Advance, MidpointRounding.AwayFromZero) + (f.AdvanceDelta ?? 0)).ShouldBe(n.Metrics.Advance);
    }

    [Fact]
    public void Blank_AndBold_Glyphs_ReportTheirAdvance()
    {
        using var native = Load();

        var space = native.RasterizeGlyph(' ', Options(HintLevel.Normal))!;
        space.Width.ShouldBe(0);
        space.Fractional!.Value.Advance.ShouldBeGreaterThan(0);

        var regular = native.RasterizeGlyph('n', Options(HintLevel.None))!.Fractional!.Value;
        var bold = native.RasterizeGlyph('n', Options(HintLevel.None) with { Bold = true })!.Fractional!.Value;
        bold.Advance.ShouldBeGreaterThan(regular.Advance, "synthetic bold grows the advance");
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
