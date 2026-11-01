using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 188 — <see cref="HintLevel.Normal"/> on Native end to end: the both-axis auto-hinter
/// with rebuilt advances, in grayscale and mono, for TrueType, CFF and variable instances, and
/// its taper at large sizes.
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
    public void Grayscale_RebuildsTheAdvance_FromTheFittedStems()
    {
        // 'h' at 9.1 ppem: linear advance 5.01 px rounds to 5, but with both stems moved into
        // whole columns the right side bearing no longer fits, and the advance becomes 6 — the
        // advance FreeType's auto-hinter gives it.
        using var native = Load(TestFonts.RobotoRegularBytes());

        native.RasterizeGlyph('h', Unhinted(RobotoCell12))!.Metrics.Advance.ShouldBe(5);
        var h = native.RasterizeGlyph('h', Normal(RobotoCell12))!;
        h.Metrics.Advance.ShouldBe(6);
        h.Metrics.BearingX.ShouldBe(1);
        h.Width.ShouldBe(4);
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
    public void Advances_StayWithinAPixelOfUnhinted()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (float size in new[] { 8f, 12f, 16f, 24f })
        {
            foreach (char c in GlyphSample)
            {
                int hinted = native.RasterizeGlyph(c, Normal(size))!.Metrics.Advance;
                int unhinted = native.RasterizeGlyph(c, Unhinted(size))!.Metrics.Advance;
                hinted.ShouldBeInRange(unhinted - 1, unhinted + 1, $"'{c}' at {size}px");
            }
        }
    }

    [Fact]
    public void LargeSizes_AreNotHinted()
    {
        // Hinting tapers off from 24 to 40 ppem: at a 64 px cell (48 ppem) Normal is exactly
        // unhinted, bitmaps and metrics alike, so large text is never distorted.
        using var native = Load(TestFonts.RobotoRegularBytes());
        foreach (char c in GlyphSample)
        {
            var normal = native.RasterizeGlyph(c, Normal(64f))!;
            var unhinted = native.RasterizeGlyph(c, Unhinted(64f))!;
            normal.BitmapData.ShouldBe(unhinted.BitmapData, $"'{c}'");
            normal.Metrics.ShouldBe(unhinted.Metrics, $"'{c}'");
        }
    }

    [Fact]
    public void MidSizes_TaperTowardsUnhinted()
    {
        // At a 48 px cell (36 ppem) the strength is a quarter: only edges already within a
        // quarter pixel of the grid snap, and the advance is the linear one.
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
