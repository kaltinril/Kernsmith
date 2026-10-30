using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// A composite glyph with a <c>USE_MY_METRICS</c> component takes that component's horizontal
/// metrics — its advance as well as its left side bearing — and ignores its own <c>hmtx</c>
/// entry, as FreeType does. At a variation instance the advance is the component's varied one:
/// its <c>HVAR</c> delta, or its phantom points when the font has no <c>HVAR</c>.
/// </summary>
/// <remarks>
/// Roboto's and Roboto Flex's 'À' set <c>USE_MY_METRICS</c> on their 'A' component; the tests
/// move one glyph's <c>hmtx</c> advance so the composite's own entry and the component's disagree.
/// FreeType runs unhinted, as in <see cref="NativeMetricsAgreementTests"/>.
/// </remarks>
public class UseMyMetricsAdvanceTests
{
    /// <summary>384 units is exactly 12 pixels at 64px in Roboto's 2048-unit em.</summary>
    private const int Shift = 384;

    private static readonly RasterOptions At64 = new() { Size = 64, Dpi = 72, EnableHinting = false };

    private static readonly Dictionary<string, float> Black = new() { ["wght"] = 1000f };

    [Fact]
    public void Static_TheCompositesOwnAdvance_IsIgnored()
    {
        var bytes = TestFonts.RobotoRegularBytes();

        var shifted = SyntheticFonts.WithAdvanceWidthShifted(bytes, GlyphIndex(bytes, 'À'), Shift);

        AssertAdvance(bytes, shifted, 'À', expectedPixels: 0);
    }

    [Fact]
    public void Static_TheComponentsAdvance_IsUsed()
    {
        var bytes = TestFonts.RobotoRegularBytes();

        var shifted = SyntheticFonts.WithAdvanceWidthShifted(bytes, GlyphIndex(bytes, 'A'), Shift);

        AssertAdvance(bytes, shifted, 'À', expectedPixels: Shift * 64 / 2048);
    }

    [Fact]
    public void Variable_WithHvar_TheCompositesOwnAdvance_IsIgnored()
    {
        var bytes = TestFonts.RobotoFlexBytes();

        var shifted = SyntheticFonts.WithAdvanceWidthShifted(bytes, GlyphIndex(bytes, 'À'), Shift);

        AssertAdvance(bytes, shifted, 'À', expectedPixels: 0, Black);
        AssertMatchesComponent(shifted, Black);
    }

    [Fact]
    public void Variable_WithoutHvar_TheComponentsPhantomPointsCarryTheAdvance()
    {
        // No HVAR: the varied advance comes from gvar phantom points — the component's.
        var bytes = SyntheticFonts.WithTables(TestFonts.RobotoFlexBytes(), new Dictionary<string, byte[]?> { ["HVAR"] = null });

        var shifted = SyntheticFonts.WithAdvanceWidthShifted(bytes, GlyphIndex(bytes, 'À'), Shift);

        AssertAdvance(bytes, shifted, 'À', expectedPixels: 0, Black);
        AssertMatchesComponent(shifted, Black);
    }

    [Theory]
    [InlineData("RobotoFlex", "wght=700,wdth=25", false)]
    [InlineData("RobotoFlex", "wght=700,wdth=25", true)]
    [InlineData("RobotoFlex", "wght=100", false)]
    [InlineData("DancingScript", "wght=700", false)]
    [InlineData("DancingScript", "wght=700", true)]
    public void Variable_EveryUseMyMetricsComposite_AdvancesAsFreeTypeDoes(string font, string axesText, bool withoutHvar)
    {
        // These fonts' composites vary their own phantom points (and HVAR entries) differently
        // from their USE_MY_METRICS component's, so at these instances the two advances part —
        // by up to a few hundred units in Roboto Flex. At one pixel per unit, FreeType's is the
        // component's.
        var bytes = font == "RobotoFlex" ? TestFonts.RobotoFlexBytes() : TestFonts.DancingScriptBytes();
        if (withoutHvar)
            bytes = SyntheticFonts.WithTables(bytes, new Dictionary<string, byte[]?> { ["HVAR"] = null });
        var axes = axesText.Split(',').ToDictionary(
            a => a.Split('=')[0], a => float.Parse(a.Split('=')[1], CultureInfo.InvariantCulture));
        var face = NativeFontFace.Load(bytes);
        var options = new RasterOptions { Size = face.Head.UnitsPerEm, Dpi = 72, EnableHinting = false };

        using var native = Native(bytes, axes);
        using var freeType = FreeType(bytes, axes);

        var failures = new StringBuilder();
        int compared = 0;
        foreach (int codepoint in BmFont.ReadFontInfo(bytes).AvailableCodepoints.OrderBy(c => c))
        {
            int glyphIndex = face.GetGlyphIndex(codepoint);
            var parsed = face.GetGlyph(glyphIndex);
            if (!parsed.IsComposite || parsed.Origin.MetricsGlyph == glyphIndex)
                continue;

            compared++;
            int expected = freeType.GetGlyphMetrics(codepoint, options)!.Value.Advance;
            int actual = native.GetGlyphMetrics(codepoint, options)!.Value.Advance;
            if (Math.Abs(expected - actual) > 1)
                failures.Append(CultureInfo.InvariantCulture, $"U+{codepoint:X4}: FreeType {expected}, native {actual}\n");
        }

        compared.ShouldBeGreaterThan(300, "the font should carry hundreds of USE_MY_METRICS composites");
        failures.ToString().ShouldBeEmpty();
    }

    /// <summary>
    /// Renders <paramref name="codepoint"/> from both fonts: the advance must move by exactly
    /// <paramref name="expectedPixels"/> and nothing else change, with <c>GetGlyphMetrics</c>
    /// agreeing and FreeType reporting the same advance for the modified font.
    /// </summary>
    private static void AssertAdvance(
        byte[] original, byte[] modified, int codepoint, int expectedPixels, Dictionary<string, float>? axes = null)
    {
        using var before = Native(original, axes);
        using var after = Native(modified, axes);

        var was = before.RasterizeGlyph(codepoint, At64)!;
        var now = after.RasterizeGlyph(codepoint, At64)!;

        now.Metrics.ShouldBe(was.Metrics with { Advance = was.Metrics.Advance + expectedPixels });
        now.BitmapData.ShouldBe(was.BitmapData);
        after.GetGlyphMetrics(codepoint, At64).ShouldBe(now.Metrics);

        using var freeType = FreeType(modified, axes);
        freeType.GetGlyphMetrics(codepoint, At64)!.Value.Advance.ShouldBe(now.Metrics.Advance, "FreeType advances it differently");
    }

    /// <summary>At the instance, 'À' advances exactly as its 'A' component does.</summary>
    private static void AssertMatchesComponent(byte[] font, Dictionary<string, float> axes)
    {
        using var native = Native(font, axes);

        native.GetGlyphMetrics('À', At64)!.Value.Advance.ShouldBe(native.GetGlyphMetrics('A', At64)!.Value.Advance);
    }

    private static int GlyphIndex(byte[] font, int codepoint) => NativeFontFace.Load(font).GetGlyphIndex(codepoint);

    private static NativeRasterizer Native(byte[] bytes, Dictionary<string, float>? axes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (axes is not null)
            native.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, axes);
        return native;
    }

    private static FreeTypeRasterizer FreeType(byte[] bytes, Dictionary<string, float>? axes)
    {
        var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        if (axes is not null)
            freeType.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, axes);
        return freeType;
    }
}
