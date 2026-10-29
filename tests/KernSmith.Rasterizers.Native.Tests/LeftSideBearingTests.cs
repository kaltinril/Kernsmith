using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// A TrueType outline is placed by its left side bearing phantom point, not by its
/// <c>glyf</c> xMin: pp1 sits at <c>xMin - lsb</c> and is the horizontal origin, so the outline
/// lands with its xMin at the <c>hmtx</c> bearing even when the two disagree. A composite with a
/// <c>USE_MY_METRICS</c> component takes that component's phantom points instead of its own.
/// </summary>
/// <remarks>
/// FreeType runs unhinted, as in <see cref="NativeMetricsAgreementTests"/>.
/// </remarks>
public class LeftSideBearingTests
{
    /// <summary>A deliberate mismatch: 384 units is exactly 12 pixels at 64px in a 2048-unit em.</summary>
    private const int Shift = 384;

    private const int ShiftPixels = 12;

    private static readonly RasterOptions At64 = new() { Size = 64, Dpi = 72, EnableHinting = false };

    [Fact]
    public void Roboto_EveryGlyph_SitsWhereFreeTypePutsIt_AtOneUnitPerPixel()
    {
        // At 2048px one font unit is one pixel, so Roboto's one-unit lsb/xMin disagreements show
        // up as a one-pixel bearing difference; every other glyph already agrees exactly.
        var bytes = TestFonts.RobotoRegularBytes();
        var options = new RasterOptions { Size = 2048, Dpi = 72, EnableHinting = false };
        var face = NativeFontFace.Load(bytes);

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var failures = new StringBuilder();
        var seen = new HashSet<int>();
        int mismatched = 0;

        for (int codepoint = 0x20; codepoint <= 0xFFFF; codepoint++)
        {
            int glyphIndex = face.GetGlyphIndex(codepoint);
            if (glyphIndex == 0 || !seen.Add(glyphIndex))
                continue;

            var parsed = face.GetGlyph(glyphIndex);
            if (parsed.IsEmpty)
                continue;

            var expected = freeType.GetGlyphMetrics(codepoint, options)!.Value;
            var actual = native.GetGlyphMetrics(codepoint, options)!.Value;
            if (actual.BearingX != expected.BearingX)
            {
                failures.Append(CultureInfo.InvariantCulture,
                    $"U+{codepoint:X4} (glyph {glyphIndex}): FreeType bearingX {expected.BearingX}, native {actual.BearingX}\n");
            }

            if (face.Hmtx.GetLeftSideBearing(glyphIndex) == parsed.XMin)
                continue;

            // The glyphs this is about: the bitmap must agree with GetGlyphMetrics too.
            mismatched++;
            var rendered = native.RasterizeGlyph(codepoint, options)!;
            rendered.Metrics.ShouldBe(actual, $"U+{codepoint:X4}: RasterizeGlyph and GetGlyphMetrics disagree");
        }

        // Guards the comparison: Roboto must actually exercise the mismatch, simple and composite.
        mismatched.ShouldBeGreaterThanOrEqualTo(40);
        failures.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Decoder_PlacesTheOutlineAtTheBearing_NotAtXMin()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());

        // The first simple glyph whose bearing and xMin disagree.
        int glyphIndex = Enumerable.Range(0, face.Maxp.NumGlyphs).First(g =>
        {
            var p = face.GetGlyph(g);
            return !p.IsEmpty && !p.IsComposite && face.Hmtx.GetLeftSideBearing(g) != p.XMin;
        });
        var parsed = face.GetGlyph(glyphIndex);
        short lsb = face.Hmtx.GetLeftSideBearing(glyphIndex);
        float shift = lsb - parsed.XMin;

        var decoded = new TrueTypeOutlineDecoder(face).Decode(glyphIndex, At64);

        decoded.DeclaredBounds.ShouldBe(new OutlineBounds(lsb, parsed.YMin, parsed.XMax + shift, parsed.YMax));
        decoded.Outline.XMin.ShouldBe(OutlineExtractor.Extract(parsed).XMin + shift);
        decoded.Outline.YMin.ShouldBe(OutlineExtractor.Extract(parsed).YMin);
    }

    [Fact]
    public void SimpleGlyph_LargeMismatch_MovesTheBitmapByExactlyTheDifference()
    {
        var bytes = TestFonts.RobotoRegularBytes();
        int glyphIndex = NativeFontFace.Load(bytes).GetGlyphIndex('H');

        var shifted = SyntheticFonts.WithLeftSideBearingShifted(bytes, glyphIndex, Shift);

        AssertShifted(bytes, shifted, 'H', ShiftPixels);
    }

    [Fact]
    public void Composite_WithoutUseMyMetrics_UsesItsOwnBearing()
    {
        // Roboto's en dash is a composite with no USE_MY_METRICS component, so its own hmtx entry
        // places it.
        var bytes = TestFonts.RobotoRegularBytes();
        var face = NativeFontFace.Load(bytes);
        int glyphIndex = face.GetGlyphIndex(0x2013);
        face.GetGlyph(glyphIndex).IsComposite.ShouldBeTrue();

        var shifted = SyntheticFonts.WithLeftSideBearingShifted(bytes, glyphIndex, Shift);

        AssertShifted(bytes, shifted, 0x2013, ShiftPixels);
    }

    [Fact]
    public void Composite_WithUseMyMetrics_IgnoresItsOwnBearing()
    {
        // Roboto's 'À' sets USE_MY_METRICS on its 'A' component: the composite's own lsb is not
        // what places it.
        var bytes = TestFonts.RobotoRegularBytes();
        var face = NativeFontFace.Load(bytes);
        int glyphIndex = face.GetGlyphIndex('À');
        face.GetGlyph(glyphIndex).IsComposite.ShouldBeTrue();

        var shifted = SyntheticFonts.WithLeftSideBearingShifted(bytes, glyphIndex, Shift);

        AssertShifted(bytes, shifted, 'À', expectedPixels: 0);
    }

    [Fact]
    public void Composite_WithUseMyMetrics_FollowsTheComponentsBearing()
    {
        // ...the 'A' component's is: shifting A's bearing moves 'À' with it.
        var bytes = TestFonts.RobotoRegularBytes();
        int component = NativeFontFace.Load(bytes).GetGlyphIndex('A');

        var shifted = SyntheticFonts.WithLeftSideBearingShifted(bytes, component, Shift);

        AssertShifted(bytes, shifted, 'À', ShiftPixels);
    }

    [Fact]
    public void VariableFont_AddsTheBearingToTheVariedOrigin()
    {
        // At wght 1000 the phantom points move too; the default-instance bearing still applies on
        // top of the varied origin.
        var bytes = TestFonts.RobotoFlexBytes();
        int glyphIndex = NativeFontFace.Load(bytes).GetGlyphIndex('H');
        var axes = new Dictionary<string, float> { ["wght"] = 1000f };

        var shifted = SyntheticFonts.WithLeftSideBearingShifted(bytes, glyphIndex, Shift);

        AssertShifted(bytes, shifted, 'H', ShiftPixels, axes);
    }

    /// <summary>
    /// Renders <paramref name="codepoint"/> from both fonts: the bitmap and advance must be
    /// unchanged and the bearing moved by exactly <paramref name="expectedPixels"/>, with
    /// <c>GetGlyphMetrics</c> agreeing and FreeType placing the modified glyph identically.
    /// </summary>
    private static void AssertShifted(
        byte[] original, byte[] modified, int codepoint, int expectedPixels, Dictionary<string, float>? axes = null)
    {
        using var before = Native(original, axes);
        using var after = Native(modified, axes);

        var was = before.RasterizeGlyph(codepoint, At64)!;
        var now = after.RasterizeGlyph(codepoint, At64)!;

        now.Metrics.BearingX.ShouldBe(was.Metrics.BearingX + expectedPixels, "bearing did not move by the mismatch");
        now.Metrics.BearingY.ShouldBe(was.Metrics.BearingY);
        now.Metrics.Advance.ShouldBe(was.Metrics.Advance);
        (now.Width, now.Height).ShouldBe((was.Width, was.Height));
        now.BitmapData.ShouldBe(was.BitmapData, "the ink itself must not change, only its placement");
        after.GetGlyphMetrics(codepoint, At64).ShouldBe(now.Metrics);

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(modified);
        if (axes is not null)
            freeType.SetVariationAxes(BmFont.ReadFontInfo(modified).VariationAxes!, axes);
        freeType.RasterizeGlyph(codepoint, At64)!.Metrics.BearingX.ShouldBe(now.Metrics.BearingX, "FreeType places it elsewhere");
    }

    private static NativeRasterizer Native(byte[] bytes, Dictionary<string, float>? axes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (axes is not null)
            native.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, axes);
        return native;
    }
}
