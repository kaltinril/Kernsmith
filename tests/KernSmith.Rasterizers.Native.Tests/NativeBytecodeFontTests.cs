using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// "The font already knows": under <see cref="HintLevel.Normal"/>, a font with TrueType
/// bytecode whose <c>head.flags</c> bit 3 asks for integer ppem is fitted and rendered at the
/// integer ppem its designer hinted for (as FreeType's TrueType driver and GDI do), takes its
/// advances as the rounded linear advance at that integer ppem in both anti-alias modes — never
/// from <c>hdmx</c>, whose advances go with the bytecode's own outlines, which Native does not
/// draw — and is not fitted at the sizes its <c>gasp</c> table
/// says not to grid-fit. Autohint fonts (Roboto) and every other level are untouched. The
/// Windows system fonts are the only fonts with all three tables; the tests skip without them.
/// </summary>
public class NativeBytecodeFontTests
{
    private const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>At most this many of the printable ASCII glyphs may disagree with the FreeType oracle.</summary>
    private const int OracleSlack = 5;

    public static TheoryData<string> VerdanaAndTahoma => ["verdana.ttf", "tahoma.ttf"];

    public static TheoryData<string> HdmxFonts => ["arial.ttf", "georgia.ttf", "tahoma.ttf"];

    public static TheoryData<string> AllFour => ["arial.ttf", "georgia.ttf", "tahoma.ttf", "verdana.ttf"];

    private static RasterOptions Normal(float size, AntiAliasMode mode = AntiAliasMode.Grayscale) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.Normal };

    private static RasterOptions Unhinted(float size, AntiAliasMode mode = AntiAliasMode.Grayscale) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.None };

    /// <summary>FreeType's own hinted output: the font's bytecode (<c>FT_LOAD_DEFAULT</c>; <c>FT_LOAD_TARGET_MONO</c> in bilevel).</summary>
    private static RasterOptions FreeTypeHinted(float size, AntiAliasMode mode) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.Full };

    [Theory]
    [MemberData(nameof(VerdanaAndTahoma))]
    public void XHeightAndCapRows_MatchTheDesigners_AtEveryTextSize(string file)
    {
        // Verdana and Tahoma are hinted for integer ppem (head.flags bit 3): their designers
        // chose the x-height and cap-height row counts per ppem, and FreeType's interpreter
        // renders exactly those. At the fractional ppem a cell size gives (12 px = 11.52 ppem)
        // our rounded-up x-height was one row off at a third of the sizes; at round(ppem) with
        // the x-height rounded to nearest the rows are the designer's at every cell from 8 to 16.
        var bytes = SystemFont(file);
        using var native = Load(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);

        var xMismatches = new StringBuilder();
        var capMismatches = new StringBuilder();
        for (int cell = 8; cell <= 16; cell++)
        {
            float size = SizeForCell(native, cell);
            int nativeX = native.RasterizeGlyph('x', Normal(size))!.Height;
            int freeTypeX = freeType.RasterizeGlyph('x', FreeTypeHinted(size, AntiAliasMode.Grayscale))!.Height;
            if (nativeX != freeTypeX)
                xMismatches.Append(CultureInfo.InvariantCulture, $"{cell}px: {nativeX} vs {freeTypeX}; ");

            int nativeH = native.RasterizeGlyph('H', Normal(size))!.Height;
            int freeTypeH = freeType.RasterizeGlyph('H', FreeTypeHinted(size, AntiAliasMode.Grayscale))!.Height;
            if (nativeH != freeTypeH)
                capMismatches.Append(CultureInfo.InvariantCulture, $"{cell}px: {nativeH} vs {freeTypeH}; ");
        }

        xMismatches.ToString().ShouldBeEmpty($"{file} x-height rows (Native vs FreeType)");
        capMismatches.ToString().ShouldBeEmpty($"{file} cap rows (Native vs FreeType)");
    }

    [Theory]
    [MemberData(nameof(HdmxFonts))]
    public void Advances_AreTheLinearOnesAtTheIntegerPpem_InBothModes_NotHdmx(string file)
    {
        // hdmx records the advances that go with the bytecode's own (widened) outlines, which
        // Native does not draw: with Native's shapes those advances set text loose in both
        // modes. The advance is the linear one at round(ppem), rounded once, in grayscale and
        // bilevel alike — at every cell with an hdmx record, where the table would say otherwise.
        var bytes = SystemFont(file);
        using var native = Load(bytes);
        var face = native.Face!;
        var profile = HintingProfile.For(face);

        int cellsWithARecord = 0;
        int hdmxDisagrees = 0;
        for (int cell = 10; cell <= 18; cell++)
        {
            float size = SizeForCell(native, cell);
            int integerPpem = (int)MathF.Floor(size + 0.5f);
            if (!profile.Hdmx!.HasRecord(integerPpem))
                continue;

            cellsWithARecord++;
            foreach (char c in Ascii)
            {
                int glyph = face.GetGlyphIndex(c);
                int linearAtInteger = GlyphPipeline.PixelAdvance(face.Hmtx.GetAdvanceWidth(glyph), integerPpem / (float)face.Head.UnitsPerEm);
                native.RasterizeGlyph(c, Normal(size, AntiAliasMode.None))!.Metrics.Advance.ShouldBe(linearAtInteger, $"{file} '{c}' at {cell}px ({size:F2} -> {integerPpem} ppem) bilevel");
                native.RasterizeGlyph(c, Normal(size))!.Metrics.Advance.ShouldBe(linearAtInteger, $"{file} '{c}' at {cell}px ({size:F2} -> {integerPpem} ppem) grayscale");
                if (profile.HdmxAdvance(integerPpem, glyph)!.Value != linearAtInteger)
                    hdmxDisagrees++;
            }
        }

        cellsWithARecord.ShouldBeGreaterThanOrEqualTo(4, $"{file}: too few cells had an hdmx record to test");
        hdmxDisagrees.ShouldBeGreaterThan(0, $"{file}: hdmx never differs from the linear advance at these cells, so the test cannot tell the two apart");
    }

    [Theory]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.None)]
    public void Georgia12px_LineIsAsLongAsUnhinted_AndTheInkIsUntouched(AntiAliasMode mode)
    {
        // Georgia's instructions widen its letters at small sizes and hdmx records the advances
        // that go with those widened shapes (FreeType's bilevel h, n, c, r, w are a pixel wider
        // than Native's at a 12 px cell). Under hdmx a line of Native's narrower ink ran 158 px
        // in bilevel and 155 in grayscale against the unhinted line at the same ppem: visibly
        // loose. The advance rule changes nothing but the advance: the line is the unhinted
        // one's within 3%, and the fitted ink is the width it was.
        var bytes = SystemFont("georgia.ttf");
        using var native = Load(bytes);
        float size = SizeForCell(native, 12);
        float integerPpem = MathF.Floor(size + 0.5f);
        integerPpem.ShouldBe(11f);

        var widths = new StringBuilder();
        foreach (char c in "hncrw")
            widths.Append(c).Append(native.RasterizeGlyph(c, Normal(size, mode))!.Width).Append(' ');
        widths.ToString().ShouldBe(mode == AntiAliasMode.None ? "h6 n6 c4 r4 w8 " : "h6 n6 c5 r6 w9 ", $"georgia {mode} at 12px ink widths");

        int hinted = LineLength(native, Normal(size, mode));
        int unhinted = LineLength(native, Unhinted(integerPpem, mode));
        Math.Abs(hinted - unhinted).ShouldBeLessThanOrEqualTo((int)(0.03 * unhinted), $"georgia {mode} at 12px: line {hinted} px vs {unhinted} px unhinted at {integerPpem} ppem");
    }

    [Theory]
    [MemberData(nameof(AllFour))]
    public void GrayscaleAdvances_MatchFreeTypesGrayscale(string file)
    {
        // FreeType's grayscale (v40) keeps the linear advances at the integer ppem; so does
        // Normal, so the two agree on nearly every printable ASCII glyph. The leftovers are
        // exact half pixels, which FreeType's 26.6 scaling rounds up and Native's float
        // rounds down (Arial's 1479-unit caps at 9 ppem are 6.4995 px: 6 here, 7 there).
        var bytes = SystemFont(file);
        using var native = Load(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);

        foreach (int cell in new[] { 10, 12, 14 })
        {
            float size = SizeForCell(native, cell);
            int matches = 0;
            var differences = new StringBuilder();
            foreach (char c in Ascii)
            {
                int actual = native.RasterizeGlyph(c, Normal(size))!.Metrics.Advance;
                int oracle = freeType.RasterizeGlyph(c, FreeTypeHinted(size, AntiAliasMode.Grayscale))!.Metrics.Advance;
                if (actual == oracle)
                    matches++;
                else
                    differences.Append(CultureInfo.InvariantCulture, $"'{c}' {actual} vs {oracle} ");
            }

            matches.ShouldBeGreaterThanOrEqualTo(80, $"{file} grayscale at {cell}px ({size:F2} ppem) vs FreeType grayscale: {differences}");
        }
    }

    /// <summary>The summed advances of a pangram, in pixels, under the given request.</summary>
    private static int LineLength(NativeRasterizer native, RasterOptions options) =>
        "The quick brown fox jumps over the lazy dog".Sum(c => native.RasterizeGlyph(c, options)!.Metrics.Advance);

    [Fact]
    public void Advances_AreTheLinearOnesAtTheIntegerPpem_WithoutAnHdmxRecord()
    {
        // Verdana ships no hdmx, so FreeType's interpreter reports the font's linear advance
        // scaled at round(ppem), rounded once — the same advance Normal gives every integer-ppem face.
        var bytes = SystemFont("verdana.ttf");
        using var native = Load(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        var face = native.Face!;

        for (int cell = 8; cell <= 16; cell++)
        {
            float size = SizeForCell(native, cell);
            float integerPpem = MathF.Floor(size + 0.5f);
            int matches = 0;
            var differences = new StringBuilder();
            foreach (char c in Ascii)
            {
                int glyph = face.GetGlyphIndex(c);
                int linearAtInteger = GlyphPipeline.PixelAdvance(face.Hmtx.GetAdvanceWidth(glyph), integerPpem / face.Head.UnitsPerEm);
                int actual = native.RasterizeGlyph(c, Normal(size))!.Metrics.Advance;
                actual.ShouldBe(linearAtInteger, $"'{c}' at {cell}px ({size:F2} -> {integerPpem} ppem)");
                int oracle = freeType.RasterizeGlyph(c, FreeTypeHinted(size, AntiAliasMode.None))!.Metrics.Advance;
                if (actual == oracle)
                    matches++;
                else
                    differences.Append(CultureInfo.InvariantCulture, $"'{c}' {actual} vs {oracle} ");
            }

            if (cell <= 10)
                matches.ShouldBeGreaterThanOrEqualTo(Ascii.Length - OracleSlack, $"verdana at {cell}px: {differences}");
        }
    }

    [Theory]
    [MemberData(nameof(AllFour))]
    public void HintingOff_KeepsTheFractionalPpem_AndTheLinearAdvances(string file)
    {
        // Default with hinting off is untouched by any of this: the glyph is scaled at the
        // fractional ppem, the advance is the linear one rounded once, and the output is exactly
        // HintLevel.None's. The integer-ppem advances differ from these for some glyphs, which
        // is what shows the rule is live under Normal and absent here.
        var bytes = SystemFont(file);
        using var native = Load(bytes);
        var face = native.Face!;
        float size = SizeForCell(native, 12);
        float scale = size / face.Head.UnitsPerEm;

        int differentUnderNormal = 0;
        foreach (char c in Ascii)
        {
            var off = native.RasterizeGlyph(c, new RasterOptions { Size = size, EnableHinting = false })!;
            var none = native.RasterizeGlyph(c, Unhinted(size))!;
            off.BitmapData.ShouldBe(none.BitmapData, $"'{c}'");
            off.Metrics.ShouldBe(none.Metrics, $"'{c}'");
            off.Metrics.Advance.ShouldBe(GlyphPipeline.PixelAdvance(face.Hmtx.GetAdvanceWidth(face.GetGlyphIndex(c)), scale), $"'{c}'");
            if (native.RasterizeGlyph(c, Normal(size))!.Metrics.Advance != off.Metrics.Advance)
                differentUnderNormal++;
        }

        differentUnderNormal.ShouldBeGreaterThan(0, $"{file}: Normal's advances never differ from the linear ones at a 12 px cell");
    }

    [Fact]
    public void GaspFloor_LeavesTheSmallSizesUnfitted_AtTheIntegerPpem()
    {
        // Verdana's gasp says no grid-fitting at 8 ppem and under. A 10 px cell is 7.68 ppem,
        // which rounds to 8: Normal renders the glyph at 8 ppem with nothing fitted — in bilevel
        // the exact bytes and metrics HintLevel.None gives at 8 ppem, down to the threshold
        // renderer (the stroke renderer draws only fitted outlines). At 9 ppem gasp asks for
        // grid-fitting again and Normal differs from None on most glyphs.
        var bytes = SystemFont("verdana.ttf");
        using var native = Load(bytes);
        float cell10 = SizeForCell(native, 10);
        MathF.Floor(cell10 + 0.5f).ShouldBe(8f);

        foreach (char c in Ascii)
        {
            var normal = native.RasterizeGlyph(c, Normal(cell10, AntiAliasMode.None))!;
            var unfitted = native.RasterizeGlyph(c, Unhinted(8f, AntiAliasMode.None))!;
            normal.Metrics.ShouldBe(unfitted.Metrics, $"'{c}' bilevel");
            normal.BitmapData.ShouldBe(unfitted.BitmapData, $"'{c}' bilevel");

            var gray = native.RasterizeGlyph(c, Normal(cell10))!;
            var grayUnfitted = native.RasterizeGlyph(c, Unhinted(8f))!;
            gray.Metrics.Advance.ShouldBe(grayUnfitted.Metrics.Advance, $"'{c}' grayscale advance");
            gray.Height.ShouldBe(grayUnfitted.Height, $"'{c}' grayscale rows");
        }

        int fitted = 0;
        foreach (char c in Ascii)
        {
            if (!native.RasterizeGlyph(c, Normal(9f, AntiAliasMode.None))!.BitmapData.AsSpan().SequenceEqual(native.RasterizeGlyph(c, Unhinted(9f, AntiAliasMode.None))!.BitmapData))
                fitted++;
        }

        fitted.ShouldBeGreaterThan(Ascii.Length / 2, "at 9 ppem gasp asks for grid-fitting, so Normal must differ from None");
    }

    [Theory]
    [MemberData(nameof(AllFour))]
    public void Metrics_MatchTheBitmap_InBothModes(string file)
    {
        // GetGlyphMetrics runs the same stages as RasterizeGlyph: the integer ppem, the
        // advance at it and the gasp decision must all be visible to both.
        var bytes = SystemFont(file);
        using var native = Load(bytes);
        var failures = new StringBuilder();
        foreach (int cell in new[] { 8, 10, 12, 14, 16 })
        {
            float size = SizeForCell(native, cell);
            foreach (var mode in new[] { AntiAliasMode.Grayscale, AntiAliasMode.None })
            {
                foreach (char c in Ascii)
                {
                    var glyph = native.RasterizeGlyph(c, Normal(size, mode))!;
                    var measured = native.GetGlyphMetrics(c, Normal(size, mode))!.Value;
                    if (measured != glyph.Metrics || measured.Width != glyph.Width || measured.Height != glyph.Height)
                        failures.Append(CultureInfo.InvariantCulture, $"'{c}' {cell}px {mode}: GetGlyphMetrics {measured} vs bitmap {glyph.Metrics}\n");
                }
            }
        }

        failures.ToString().ShouldBeEmpty(file);
    }

    [Fact]
    public void SyntheticBold_TakesTheLinearAdvanceAtTheIntegerPpem()
    {
        // An emboldened glyph's advance is its own (grown) linear advance at the integer ppem,
        // which is what None reports at that size.
        var bytes = SystemFont("arial.ttf");
        using var native = Load(bytes);
        float size = SizeForCell(native, 12);
        float integerPpem = MathF.Floor(size + 0.5f);

        foreach (char c in Ascii)
        {
            var bold = native.RasterizeGlyph(c, Normal(size) with { Bold = true })!;
            bold.Metrics.Advance.ShouldBe(native.RasterizeGlyph(c, Unhinted(integerPpem) with { Bold = true })!.Metrics.Advance, $"'{c}'");
            native.GetGlyphMetrics(c, Normal(size) with { Bold = true })!.Value.ShouldBe(bold.Metrics, $"'{c}'");
        }
    }

    [Fact]
    public void VariableInstance_IsFittedAtTheIntegerPpem()
    {
        // Roboto Flex carries a prep table and head.flags bit 3, so FreeType's interpreter (with
        // nothing to execute) renders it at the integer ppem; so does Normal, at every instance,
        // with the instance's own (HVAR) advance scaled at that ppem.
        var bytes = TestFonts.RobotoFlexBytes();
        var axes = BmFont.ReadFontInfo(bytes).VariationAxes!;
        using var native = Load(bytes);
        native.SetVariationAxes(axes, new Dictionary<string, float> { ["wght"] = 1000 });
        float size = SizeForCell(native, 12);
        float integerPpem = MathF.Floor(size + 0.5f);
        integerPpem.ShouldNotBe(size);

        var different = new StringBuilder();
        foreach (char c in Ascii)
        {
            var normal = native.RasterizeGlyph(c, Normal(size))!;
            normal.Metrics.Advance.ShouldBe(native.RasterizeGlyph(c, Unhinted(integerPpem))!.Metrics.Advance, $"'{c}'");
            native.GetGlyphMetrics(c, Normal(size))!.Value.ShouldBe(normal.Metrics, $"'{c}'");

            // Fitted at the integer ppem, the glyph is the one a request for exactly that ppem gets.
            var atInteger = native.RasterizeGlyph(c, Normal(integerPpem))!;
            if (atInteger.Metrics != normal.Metrics || !atInteger.BitmapData.AsSpan().SequenceEqual(normal.BitmapData))
                different.Append(CultureInfo.InvariantCulture, $"'{c}' {normal.Metrics} vs {atInteger.Metrics}; ");
        }

        different.ToString().ShouldBeEmpty($"{size:F3} -> {integerPpem} ppem");
    }

    [Fact]
    public void StrokeRenderer_FollowsTheFit()
    {
        // The bilevel stroke renderer draws fitted outlines: a glyph the gasp table kept
        // unfitted falls back to the threshold, and the decision is made at the ppem the fit
        // ran at, so a 39.6 ppem request a bit-3 face fits at 40 (past the taper) is not stroked.
        var mono = new RasterOptions { Size = 10f, AntiAlias = AntiAliasMode.None, HintLevel = HintLevel.Normal };
        var fitted = new PreparedGlyph(Internal.Outlines.GlyphOutline.Empty, new Internal.Outlines.OutlineTransform(1f, 0f), default, 5);

        CoverageRenderer.MonoRenderingFor(fitted, mono).ShouldBe(Internal.Raster.MonoRendering.Strokes);
        CoverageRenderer.MonoRenderingFor(fitted with { FitWithheld = true }, mono).ShouldBe(Internal.Raster.MonoRendering.Threshold);
        CoverageRenderer.MonoRenderingFor(fitted with { HintPpem = 40f }, mono with { Size = 39.6f }).ShouldBe(Internal.Raster.MonoRendering.Threshold);
        CoverageRenderer.MonoRenderingFor(fitted, mono with { Size = 39.6f }).ShouldBe(Internal.Raster.MonoRendering.Strokes);
        fitted.RenderPpem(mono).ShouldBe(10f);
        (fitted with { HintPpem = 12f }).RenderPpem(mono).ShouldBe(12f);
    }

    /// <summary>The em size core derives for a BMFont cell: cell × upem / (usWinAscent + usWinDescent).</summary>
    private static float SizeForCell(NativeRasterizer native, float cell)
    {
        var face = native.Face!;
        return cell * face.Head.UnitsPerEm / (face.Os2.WinAscent + (float)face.Os2.WinDescent);
    }

    internal static byte[] SystemFont(string file)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        Assert.SkipUnless(File.Exists(path), $"{file} is not installed on this machine.");
        return File.ReadAllBytes(path);
    }

    private static NativeRasterizer Load(byte[] bytes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        return native;
    }
}
