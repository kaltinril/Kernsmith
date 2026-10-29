using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 2 — Native's CFF/CFF2 output against established backends: metrics and
/// bitmaps against FreeType for both fixtures, and bitmaps against stb_truetype (which reads
/// CFF but not CFF2) for the CFF fixture, in both grayscale and monochrome.
/// </summary>
/// <remarks>
/// Unhinted throughout: Native has no hinter, and FreeType's CFF hinter legitimately moves
/// stems and heights by a pixel or two at small sizes. Bitmaps are compared on a canvas anchored
/// at the pen, so a backend whose box is a pixel wider still lines up with the other.
/// </remarks>
public class NativeCffAgreementTests
{
    private const string GlyphSample =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789&@%?$";

    public static TheoryData<string, float> FontsAndSizes()
    {
        var data = new TheoryData<string, float>();
        foreach (string font in new[] { "serif", "code" })
        {
            foreach (float size in new[] { 12f, 16f, 24f, 32f, 48f, 96f })
                data.Add(font, size);
        }

        return data;
    }

    private static byte[] Font(string font) =>
        font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes();

    [Theory]
    [MemberData(nameof(FontsAndSizes))]
    public void GlyphMetrics_MatchFreeType_WithinOnePixel(string font, float size)
    {
        var options = new RasterOptions { Size = size, Dpi = 72, EnableHinting = false };
        var bytes = Font(font);

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var failures = new StringBuilder();
        foreach (char c in GlyphSample + " .,;:!'\"()[]{}<>#*-+=/\\|~^")
        {
            var expected = freeType.RasterizeGlyph(c, options);
            var actual = native.RasterizeGlyph(c, options);
            var measured = native.GetGlyphMetrics(c, options);
            if (expected is null)
                continue;

            if (actual is null || measured is null)
            {
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': native returned null\n");
                continue;
            }

            Compare(failures, c, "advance", expected.Metrics.Advance, actual.Metrics.Advance);
            Compare(failures, c, "width", expected.Width, actual.Width);
            Compare(failures, c, "height", expected.Height, actual.Height);
            if (expected.Width > 0 && actual.Width > 0)
            {
                Compare(failures, c, "bearingX", expected.Metrics.BearingX, actual.Metrics.BearingX);
                Compare(failures, c, "bearingY", expected.Metrics.BearingY, actual.Metrics.BearingY);
            }

            // GetGlyphMetrics must describe exactly the bitmap RasterizeGlyph produces.
            measured.Value.Width.ShouldBe(actual.Width, $"'{c}' measured width");
            measured.Value.Height.ShouldBe(actual.Height, $"'{c}' measured height");
        }

        failures.ToString().ShouldBeEmpty($"{font} metrics disagree with FreeType at {size}px");
    }

    [Theory]
    [InlineData("serif", 16f)]
    [InlineData("serif", 32f)]
    [InlineData("serif", 64f)]
    [InlineData("code", 16f)]
    [InlineData("code", 32f)]
    [InlineData("code", 64f)]
    public void GrayscaleBitmaps_MatchFreeType(string font, float size)
    {
        // Floors sit just under what Native's TrueType path scores against FreeType on Roboto
        // (worst glyph 0.907, mean 0.99+): CFF must agree at least as well as glyf does.
        using IRasterizer freeType = new FreeTypeRasterizer();
        AssertBitmapsAgree(freeType, Font(font), AntiAliasMode.Grayscale, size, "FreeType", floor: 0.90, meanFloor: 0.98);
    }

    [Theory]
    [InlineData("serif", 16f)]
    [InlineData("serif", 32f)]
    [InlineData("serif", 64f)]
    [InlineData("code", 32f)]
    [InlineData("code", 64f)]
    public void MonochromeBitmaps_TrackFreeType(string font, float size)
    {
        // Mean only, no per-glyph floor: FreeType's monochrome rasterizer samples pixel centres,
        // Native thresholds coverage at 50%, so individual edge pixels flip between them for any
        // outline format (Roboto's worst glyph scores ~0.72 the same way). Source Code at 16px is
        // left out: its variable default instance is ExtraLight, whose half-pixel hairlines both
        // keep through dropout control (NativeHintingInterplayTests), but often in neighbouring
        // pixel columns or rows, and SSIM on one-pixel strokes scores such a shift as a mismatch.
        using IRasterizer freeType = new FreeTypeRasterizer();
        AssertBitmapsAgree(freeType, Font(font), AntiAliasMode.None, size, "FreeType", floor: 0, meanFloor: 0.90);
    }

    [Theory]
    [InlineData(AntiAliasMode.Grayscale, 16f)]
    [InlineData(AntiAliasMode.Grayscale, 32f)]
    [InlineData(AntiAliasMode.Grayscale, 64f)]
    [InlineData(AntiAliasMode.None, 16f)]
    [InlineData(AntiAliasMode.None, 32f)]
    [InlineData(AntiAliasMode.None, 64f)]
    public void Bitmaps_MatchStbTrueType(AntiAliasMode mode, float size)
    {
        // stb_truetype reads CFF (not CFF2). Its monochrome is a plain threshold with no dropout
        // control, so where a thin serif or stroke falls under it Native (like FreeType) keeps
        // pixels stb drops ('b', 'k', '9', '$' at 16px): monochrome is held to the mean only.
        using IRasterizer stb = new StbTrueTypeRasterizer();
        AssertBitmapsAgree(stb, TestFonts.SourceSerifOtfBytes(), mode, size, "stb_truetype",
            floor: mode == AntiAliasMode.None ? 0 : 0.90, meanFloor: mode == AntiAliasMode.None ? 0.90 : 0.98);
    }

    [Fact]
    public void BitmapComparison_RejectsTheWrongGlyph()
    {
        // Guards the thresholds below: two different glyphs placed on the same canvas must score
        // well under them, or the comparison would pass anything.
        var options = new RasterOptions { Size = 32, EnableHinting = false };
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(TestFonts.SourceSerifOtfBytes());

        Score(native.RasterizeGlyph('o', options)!, native.RasterizeGlyph('e', options)!).ShouldBeLessThan(0.8);
    }

    private static void AssertBitmapsAgree(
        IRasterizer reference, byte[] font, AntiAliasMode mode, float size, string name, double floor, double meanFloor)
    {
        var options = new RasterOptions { Size = size, Dpi = 72, EnableHinting = false, AntiAlias = mode };
        reference.LoadFont(font);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(font);

        var failures = new StringBuilder();
        double total = 0;
        foreach (char c in GlyphSample)
        {
            var expected = reference.RasterizeGlyph(c, options);
            var actual = native.RasterizeGlyph(c, options);
            expected.ShouldNotBeNull($"{name} '{c}'");
            actual.ShouldNotBeNull($"native '{c}'");
            actual!.BitmapData.ShouldContain(b => b != 0, $"native '{c}' is blank");

            double score = Score(expected!, actual);
            total += score;
            if (score < floor)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' SSIM {score:F3}\n");
        }

        double mean = total / GlyphSample.Length;
        failures.ToString().ShouldBeEmpty($"glyphs below SSIM {floor} vs {name} ({mode}, {size}px)");
        mean.ShouldBeGreaterThan(meanFloor, $"mean SSIM vs {name} ({mode}, {size}px)");
    }

    /// <summary>SSIM of two glyphs on a shared canvas anchored at the pen position.</summary>
    private static double Score(RasterizedGlyph a, RasterizedGlyph b)
    {
        int left = Math.Min(a.Metrics.BearingX, b.Metrics.BearingX);
        int right = Math.Max(a.Metrics.BearingX + a.Width, b.Metrics.BearingX + b.Width);
        int top = Math.Max(a.Metrics.BearingY, b.Metrics.BearingY);
        int bottom = Math.Min(a.Metrics.BearingY - a.Height, b.Metrics.BearingY - b.Height);
        int width = right - left, height = top - bottom;

        var first = ImageSimilarity.Place(Coverage(a), a.Width, a.Height, a.Metrics.BearingX, a.Metrics.BearingY, left, top, width, height);
        var second = ImageSimilarity.Place(Coverage(b), b.Width, b.Height, b.Metrics.BearingX, b.Metrics.BearingY, left, top, width, height);
        return ImageSimilarity.Ssim(first, second, width, height);
    }

    /// <summary>The glyph as one coverage byte per pixel, whatever its pitch.</summary>
    private static byte[] Coverage(RasterizedGlyph glyph)
    {
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        if (glyph.Pitch == glyph.Width)
            return glyph.BitmapData;

        var packed = new byte[glyph.Width * glyph.Height];
        for (int y = 0; y < glyph.Height; y++)
            Array.Copy(glyph.BitmapData, y * glyph.Pitch, packed, y * glyph.Width, glyph.Width);

        return packed;
    }

    private static void Compare(StringBuilder failures, char character, string field, int expected, int actual)
    {
        if (Math.Abs(expected - actual) <= 1)
            return;

        failures.Append(CultureInfo.InvariantCulture, $"'{character}' {field}: expected {expected}, got {actual}\n");
    }
}
