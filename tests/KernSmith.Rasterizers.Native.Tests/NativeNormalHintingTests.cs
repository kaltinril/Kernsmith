using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 188 — <see cref="HintLevel.Normal"/> on Native end to end: the both-axis auto-hinter
/// within the font's advances, in grayscale and bilevel, for TrueType, CFF and variable
/// instances, and its taper at large sizes.
/// </summary>
public class NativeNormalHintingTests
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string GlyphSample = Letters + "0123456789 .,;:!?'\"()[]{}<>@#$%&*-+=/\\|~^";

    /// <summary>Roboto's em size for a 12 px BMFont cell (12 × 2048 / (2146 + 555)), 9.1 ppem.</summary>
    private const float RobotoCell12 = 12f * 2048f / (2146f + 555f);

    public static TheoryData<float, AntiAliasMode> SizesAndModes =>
    [
        (8f, AntiAliasMode.Grayscale), (12f, AntiAliasMode.Grayscale), (16f, AntiAliasMode.Grayscale), (24f, AntiAliasMode.Grayscale),
        (8f, AntiAliasMode.None), (12f, AntiAliasMode.None), (16f, AntiAliasMode.None), (24f, AntiAliasMode.None),
    ];

    private static RasterOptions Normal(float size, AntiAliasMode mode = AntiAliasMode.Grayscale) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.Normal };

    private static RasterOptions Unhinted(float size, AntiAliasMode mode = AntiAliasMode.Grayscale) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.None };

    [Fact]
    public void Capabilities_AdvertiseNormal()
    {
        new NativeRasterizer().Capabilities.SupportedHintLevels.ShouldContain(HintLevel.Normal);
    }

    [Theory]
    [MemberData(nameof(SizesAndModes))]
    public void Metrics_MatchTheBitmap(float size, AntiAliasMode mode)
    {
        // Normal rebuilds advances and moves boxes, so GetGlyphMetrics must run the same fit.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var failures = new StringBuilder();
        foreach (char c in GlyphSample)
        {
            var glyph = native.RasterizeGlyph(c, Normal(size, mode))!;
            var measured = native.GetGlyphMetrics(c, Normal(size, mode))!.Value;
            if (measured != glyph.Metrics || measured.Width != glyph.Width || measured.Height != glyph.Height)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' GetGlyphMetrics {measured} vs bitmap {glyph.Metrics}\n");
        }

        failures.ToString().ShouldBeEmpty($"metrics disagree with the bitmap at {size}px {mode}");
    }

    [Fact]
    public void Grayscale_PutsAThinStemInOneColumn()
    {
        // Roboto's 'l' at a 12 px cell is a 0.82 px stem. Unhinted it straddles two columns
        // (78 | 132); Normal lands it in one column at its true coverage (~210), as FreeType's
        // auto-hinter does, without widening it to a solid pixel.
        using var native = Load(TestFonts.RobotoRegularBytes());

        var l = native.RasterizeGlyph('l', Normal(RobotoCell12))!;

        l.Width.ShouldBe(1);
        foreach (byte b in l.BitmapData)
            b.ShouldBeInRange((byte)190, (byte)230);
        native.RasterizeGlyph('l', Unhinted(RobotoCell12))!.Width.ShouldBe(2);
    }

    [Fact]
    public void Grayscale_KeepsTheFontsAdvance()
    {
        // 'h' at 9.1 ppem: linear advance 5.01 px; both stems move into whole columns and the
        // advance stays 5, the 1.2 px of side bearing split around the stems. FreeType's
        // auto-hinter gives it 6 (its rebuild follows the shifted right edge), which sets "nn"
        // 2.3 px apart against 0.35 px for "oo"; keeping 5 keeps the gaps in the font's proportion.
        using var native = Load(TestFonts.RobotoRegularBytes());

        native.RasterizeGlyph('h', Unhinted(RobotoCell12))!.Metrics.Advance.ShouldBe(5);
        var h = native.RasterizeGlyph('h', Normal(RobotoCell12))!;
        h.Metrics.Advance.ShouldBe(5);
        h.Width.ShouldBe(4);
        h.Metrics.BearingX.ShouldBeInRange(0, 1);
    }

    [Fact]
    public void Grayscale_ThinStems_KeepTheirLinearAdvance()
    {
        // Roboto 'i' and 'l' at a 10 px cell (7.58 ppem): a 0.72 px stem, 1.84 px advance. The
        // fit centres the stem in a column without changing its width, so the advance must stay
        // the linear 2; rebuilt from the column's right edge it came out 1, which left 0.28 px
        // between two 'l's and made "ill" one grey block.
        using var native = Load(TestFonts.RobotoRegularBytes());
        const float cell10 = 10f * 2048f / (2146f + 555f);

        foreach (char c in "il")
        {
            int unhinted = native.RasterizeGlyph(c, Unhinted(cell10))!.Metrics.Advance;
            unhinted.ShouldBe(2, $"'{c}' unhinted");
            native.RasterizeGlyph(c, Normal(cell10))!.Metrics.Advance.ShouldBe(2, $"'{c}' hinted");
        }
    }

    public static TheoryData<float, AntiAliasMode> WordGapCells =>
    [
        (8f, AntiAliasMode.Grayscale), (10f, AntiAliasMode.Grayscale), (12f, AntiAliasMode.Grayscale), (14f, AntiAliasMode.Grayscale), (16f, AntiAliasMode.Grayscale),
        (8f, AntiAliasMode.None), (10f, AntiAliasMode.None), (12f, AntiAliasMode.None), (14f, AntiAliasMode.None), (16f, AntiAliasMode.None),
    ];

    [Theory]
    [MemberData(nameof(WordGapCells))]
    public void WordGap_IsNotNarrowerThanUnhinted(float cell, AntiAliasMode mode)
    {
        // The white between "e" and "t" across a space — e's right bearing, the space, t's left
        // bearing — must not come out narrower hinted than unhinted at any text size: letters
        // may gain white from the fit, the word gap may not lose it.
        using var native = Load(TestFonts.RobotoRegularBytes());
        float size = cell * 2048f / (2146f + 555f);

        int WordGap(RasterOptions options)
        {
            var e = native.RasterizeGlyph('e', options)!;
            var t = native.RasterizeGlyph('t', options)!;
            int space = native.RasterizeGlyph(' ', options)!.Metrics.Advance;
            return (e.Metrics.Advance - e.Metrics.BearingX - e.Width) + space + t.Metrics.BearingX;
        }

        WordGap(Normal(size, mode)).ShouldBeGreaterThanOrEqualTo(WordGap(Unhinted(size, mode)), $"{cell} px {mode}");
    }

    public static TheoryData<float, AntiAliasMode> TabularDigitCells =>
    [
        (14f, AntiAliasMode.Grayscale), (12f, AntiAliasMode.Grayscale),
        (12f, AntiAliasMode.None), (10f, AntiAliasMode.None), (8f, AntiAliasMode.None),
    ];

    [Theory]
    [MemberData(nameof(TabularDigitCells))]
    public void Digits_ShareOneAdvance(float cell, AntiAliasMode mode)
    {
        // Roboto's digits are tabular (one advance, 1150 units) and games set scores with them,
        // so they must stay one advance at every size however each digit's own fit comes out:
        // an advance rebuilt from the fitted ink once gave six digits 7 px and four 6 at a 14 px
        // cell, which reads as "01234 56789" with ragged columns.
        using var native = Load(TestFonts.RobotoRegularBytes());
        float size = cell * 2048f / (2146f + 555f);

        var advances = "0123456789".Select(c => (c, native.RasterizeGlyph(c, Normal(size, mode))!.Metrics.Advance)).ToList();

        advances.Select(a => a.Advance).Distinct().Count().ShouldBe(1,
            $"{cell} px {mode}: " + string.Join(" ", advances.Select(a => $"{a.c}={a.Advance}")));
    }

    [Fact]
    public void Mono_FitsBeforeTheThreshold()
    {
        // Roboto 'h' at 9.1 ppem in mono: two whole-pixel stems with a two-pixel counter
        // (1-2-1), the shape FreeType's mono target gives, where thresholding the unhinted
        // outline collapses the counter to one pixel.
        using var native = Load(TestFonts.RobotoRegularBytes());

        var h = native.RasterizeGlyph('h', Normal(RobotoCell12, AntiAliasMode.None))!;

        h.Width.ShouldBe(4);
        for (int row = h.Height - 3; row < h.Height; row++)
            h.BitmapData.AsSpan(row * h.Pitch, 4).ToArray().ShouldBe(new byte[] { 255, 0, 0, 255 }, $"row {row}");
    }

    [Fact]
    public void Mono_KeepsBowlsOnePixelWide()
    {
        // 'o' at 9.1 ppem: 0.82 px bowl sides 4.4 px apart become 1-2-1 in four columns, the
        // widening taken from the counter rather than added to the width.
        using var native = Load(TestFonts.RobotoRegularBytes());

        var o = native.RasterizeGlyph('o', Normal(RobotoCell12, AntiAliasMode.None))!;

        o.Width.ShouldBe(4);
        o.BitmapData.AsSpan(2 * o.Pitch, 4).ToArray().ShouldBe(new byte[] { 255, 0, 0, 255 });
    }

    [Fact]
    public void Advances_AreTheFonts_InBothModes()
    {
        // The advance is the font's, rounded once, hinted or not, grayscale or bilevel: a line
        // of hinted text is exactly as long as the unhinted one. The pairs that touch for it
        // are the kerning bake's (NativeKerningBakeTests).
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (float size in new[] { 8f, 12f, 16f, 24f })
        {
            foreach (var mode in new[] { AntiAliasMode.Grayscale, AntiAliasMode.None })
            {
                foreach (char c in GlyphSample)
                {
                    int hinted = native.RasterizeGlyph(c, Normal(size, mode))!.Metrics.Advance;
                    int unhinted = native.RasterizeGlyph(c, Unhinted(size, mode))!.Metrics.Advance;
                    hinted.ShouldBe(unhinted, $"'{c}' at {size}px {mode}");
                }
            }
        }
    }

    public static TheoryData<string, float, AntiAliasMode> LineLengthCells
    {
        get
        {
            var data = new TheoryData<string, float, AntiAliasMode>();
            foreach (string font in new[] { "Roboto", "NotoSans", "SourceSerif4", "DejaVuSansMono" })
            {
                foreach (float cell in new[] { 8f, 10f, 12f, 14f, 16f })
                {
                    data.Add(font, cell, AntiAliasMode.Grayscale);
                    data.Add(font, cell, AntiAliasMode.None);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LineLengthCells))]
    public void LineLength_StaysWithinThreePercentOfUnhinted(string font, float cell, AntiAliasMode mode)
    {
        // The owner's bound: hinting must not inflate or shrink a line. With the kerning bake
        // (the default) every letter's advance is the font's, so the letters are exactly as
        // long; the stem-pair padding that stands in when the bake is off is outside this bound.
        // The one exception is the word gap: a blank glyph rounds up at text sizes, at most a
        // pixel per space and never less than the font's.
        const string line = "The quick brown fox jumps over the lazy dog. Hamburgefonstiv 0123456789 ILLINOIS rn ill";
        var (bytes, unitsPerEm, ascent, descent) = font switch
        {
            "Roboto" => (TestFonts.RobotoRegularBytes(), 2048f, 2146f, 555f),
            "NotoSans" => (File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, "NotoSans-Regular-Latin.ttf")), 1000f, 1069f, 293f),
            "SourceSerif4" => (TestFonts.SourceSerifOtfBytes(), 1000f, 1036f, 335f),
            _ => (SystemFont("DejaVuSansMono.ttf"), 2048f, 1901f, 483f),
        };
        using var native = Load(bytes);
        float size = cell * unitsPerEm / (ascent + descent);

        int hinted = 0, unhinted = 0, hintedBlank = 0, unhintedBlank = 0;
        foreach (char c in line)
        {
            int h = native.RasterizeGlyph(c, Normal(size, mode))!.Metrics.Advance;
            int u = native.RasterizeGlyph(c, Unhinted(size, mode))!.Metrics.Advance;
            if (c == ' ')
            {
                hintedBlank += h;
                unhintedBlank += u;
            }
            else
            {
                hinted += h;
                unhinted += u;
            }
        }

        ((hinted - unhinted) * 100.0 / unhinted).ShouldBeInRange(-3.0, 3.0, $"{font} {cell} px {mode}: {unhinted} -> {hinted}");
        (hintedBlank - unhintedBlank).ShouldBeInRange(0, line.Count(c => c == ' '), $"{font} {cell} px {mode}: spaces {unhintedBlank} -> {hintedBlank}");
    }

    private static byte[] SystemFont(string file)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        Assert.SkipUnless(File.Exists(path), $"{file} is not installed on this machine.");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void LargeSizes_AreNotHinted()
    {
        // Hinting tapers off from 16 to 40 ppem: at a 64 px cell (48 ppem) Normal is exactly
        // unhinted, bitmaps and metrics alike, so large text is never distorted.
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (var mode in new[] { AntiAliasMode.Grayscale, AntiAliasMode.None })
        {
            foreach (char c in GlyphSample)
            {
                var normal = native.RasterizeGlyph(c, Normal(64f, mode))!;
                var unhinted = native.RasterizeGlyph(c, Unhinted(64f, mode))!;
                normal.BitmapData.ShouldBe(unhinted.BitmapData, $"'{c}' {mode}");
                normal.Metrics.ShouldBe(unhinted.Metrics, $"'{c}' {mode}");
            }
        }
    }

    [Fact]
    public void MidSizes_TaperTowardsUnhinted()
    {
        // At a 48 px cell (36 ppem) the x axis is long off and the y strength is 0.15: only
        // edges already within 0.15 px of a row snap, and the advance is the linear one.
        using var native = Load(TestFonts.RobotoRegularBytes());
        double ssim = 0;
        foreach (char c in Letters)
        {
            var normal = native.RasterizeGlyph(c, Normal(48f))!;
            var unhinted = native.RasterizeGlyph(c, Unhinted(48f))!;
            normal.Metrics.Advance.ShouldBe(unhinted.Metrics.Advance, $"'{c}'");
            ssim += NativeSyntheticStyleTests.SsimOnSharedCanvas(normal, unhinted);
        }

        (ssim / Letters.Length).ShouldBeGreaterThanOrEqualTo(0.99);
    }

    [Fact]
    public void Cff_IsHintedInBothAxes()
    {
        // The auto-hinter works from outlines, so a CFF font gets the same treatment: output
        // changes, stems sharpen, and metrics agree with the bitmap.
        using var native = Load(TestFonts.SourceSerifOtfBytes());
        int changed = 0;
        double hintedFuzz = 0, unhintedFuzz = 0;
        foreach (char c in Letters)
        {
            var normal = native.RasterizeGlyph(c, Normal(12f))!;
            var unhinted = native.RasterizeGlyph(c, Unhinted(12f))!;
            if (!normal.BitmapData.AsSpan().SequenceEqual(unhinted.BitmapData))
                changed++;
            hintedFuzz += NativeHintingTests.Fuzz(normal);
            unhintedFuzz += NativeHintingTests.Fuzz(unhinted);
            native.GetGlyphMetrics(c, Normal(12f))!.Value.ShouldBe(normal.Metrics, $"'{c}'");
        }

        changed.ShouldBeGreaterThan(Letters.Length / 2);
        hintedFuzz.ShouldBeLessThan(unhintedFuzz);
    }

    [Fact]
    public void VariableInstances_AreMeasuredPerInstance()
    {
        // The standard stem and zones are measured at the instance: the same glyph hinted at
        // weight 100 and 1000 differs in more than the unhinted outlines do, and each instance's
        // metrics agree with its bitmap.
        var bytes = TestFonts.RobotoFlexBytes();
        var axes = BmFont.ReadFontInfo(bytes).VariationAxes!;
        using var native = Load(bytes);

        native.SetVariationAxes(axes, new Dictionary<string, float> { ["wght"] = 1000 });
        var heavy = native.RasterizeGlyph('H', Normal(12f))!;
        native.GetGlyphMetrics('H', Normal(12f))!.Value.ShouldBe(heavy.Metrics);

        native.SetVariationAxes(axes, new Dictionary<string, float> { ["wght"] = 100 });
        var light = native.RasterizeGlyph('H', Normal(12f))!;
        native.GetGlyphMetrics('H', Normal(12f))!.Value.ShouldBe(light.Metrics);

        heavy.BitmapData.ShouldNotBe(light.BitmapData);
        // A hairline stem stays one column; the heavy stem is wider than the light one.
        ColumnsLit(heavy).ShouldBeGreaterThan(ColumnsLit(light));
    }

    private static int ColumnsLit(RasterizedGlyph glyph)
    {
        int lit = 0;
        for (int x = 0; x < glyph.Width; x++)
        {
            for (int y = 0; y < glyph.Height; y++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + x] >= 128)
                {
                    lit++;
                    break;
                }
            }
        }

        return lit;
    }

    private static NativeRasterizer Load(byte[] bytes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        return native;
    }
}
