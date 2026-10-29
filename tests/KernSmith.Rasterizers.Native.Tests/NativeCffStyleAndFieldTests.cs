using System.Globalization;
using System.Text;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// CFF outlines (Phase 166) through the later pipeline stages: synthetic bold/italic (167)
/// transform them like <c>glyf</c> outlines, and distance fields (169/170) are measured from
/// them, with <c>GetGlyphMetrics</c> describing exactly what <c>RasterizeGlyph</c> renders.
/// </summary>
public class NativeCffStyleAndFieldTests
{
    private const string StyleSample = "AOeBIgW8as";

    public static TheoryData<float, bool, bool> StyledSizes()
    {
        var data = new TheoryData<float, bool, bool>();
        foreach (float size in new[] { 16f, 32f, 64f })
        {
            data.Add(size, true, false);
            data.Add(size, false, true);
            data.Add(size, true, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StyledSizes))]
    public void StyledCffGlyphs_LookLikeFreeTypes(float size, bool bold, bool italic)
    {
        // Same bar as the Roboto case in NativeSyntheticStyleTests, relaxed to 0.9: the styled
        // glyph must look like FreeType's styled glyph, and more so than Native's unstyled one
        // does, so silently dropping the style cannot pass.
        var options = Options(size, bold, italic);
        var unstyled = Options(size, bold: false, italic: false);
        using var freeType = Load(new FreeTypeRasterizer());
        using var native = Load(new NativeRasterizer());

        var failures = new StringBuilder();
        foreach (char character in StyleSample)
        {
            var reference = freeType.RasterizeGlyph(character, options)!;
            double styled = NativeSyntheticStyleTests.SsimOnSharedCanvas(native.RasterizeGlyph(character, options)!, reference);
            double plain = NativeSyntheticStyleTests.SsimOnSharedCanvas(native.RasterizeGlyph(character, unstyled)!, reference);

            if (styled < 0.9 || styled <= plain)
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': styled {styled:F4}, unstyled {plain:F4}\n");
        }

        failures.ToString().ShouldBeEmpty($"styled CFF bitmaps differ from FreeType's ({size}px bold={bold} italic={italic})");
    }

    [Theory]
    [MemberData(nameof(StyledSizes))]
    public void StyledCffGlyphs_MeasureWhatTheyRender(float size, bool bold, bool italic)
    {
        var options = Options(size, bold, italic);
        using var native = Load(new NativeRasterizer());

        for (int codepoint = 0x21; codepoint < 0x7F; codepoint++)
        {
            var rendered = native.RasterizeGlyph(codepoint, options);
            rendered.ShouldNotBeNull($"U+{codepoint:X4}");
            native.GetGlyphMetrics(codepoint, options).ShouldBe(rendered.Metrics, $"U+{codepoint:X4}");
        }
    }

    [Theory]
    [InlineData('H')]
    [InlineData('o')]
    [InlineData('m')]
    public void BoldCffGlyph_IsWider_WithAWiderAdvance(char character)
    {
        using var native = Load(new NativeRasterizer());
        var plain = native.RasterizeGlyph(character, Options(32, bold: false, italic: false))!;
        var bold = native.RasterizeGlyph(character, Options(32, bold: true, italic: false))!;

        bold.Width.ShouldBeGreaterThan(plain.Width, "synthetic bold did not widen the CFF glyph");
        bold.Metrics.Advance.ShouldBeGreaterThan(plain.Metrics.Advance);
        Ink(bold).ShouldBeGreaterThan(Ink(plain));
    }

    [Fact]
    public void ItalicCffGlyph_IsSlanted()
    {
        // A 12° shear moves the stem tan(12°) ≈ 0.21px right per pixel up. The slope is taken
        // from the stem's ink centre on two rows clear of the serifs; upright it is ~0.
        using var native = Load(new NativeRasterizer());
        var plain = native.RasterizeGlyph('l', Options(64, bold: false, italic: false))!;
        var italic = native.RasterizeGlyph('l', Options(64, bold: false, italic: true))!;

        double plainLean = Lean(plain), italicLean = Lean(italic);
        Math.Abs(plainLean).ShouldBeLessThan(0.03, "upright 'l' should not lean");
        italicLean.ShouldBeInRange(0.19, 0.235, "synthetic italic did not slant the CFF glyph by 12°");
        italic.Width.ShouldBeGreaterThan(plain.Width);
        italic.Metrics.Advance.ShouldBe(plain.Metrics.Advance);
    }

    // ---- Distance fields -------------------------------------------------------------------

    public static TheoryData<string, SdfMode> FieldCases() => new()
    {
        { "serif", SdfMode.Sdf }, { "code", SdfMode.Sdf },
        { "serif", SdfMode.Msdf }, { "serif", SdfMode.Mtsdf },
    };

    [Theory]
    [MemberData(nameof(FieldCases))]
    public void CffDistanceField_MeasuresWhatItRenders(string font, SdfMode mode)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes());
        var options = new RasterOptions { Size = 32, Dpi = 72, SdfMode = mode, SdfSpread = 6 };
        var coverageOptions = new RasterOptions { Size = 32, Dpi = 72 };

        for (int codepoint = 0x21; codepoint < 0x7F; codepoint++)
        {
            var glyph = native.RasterizeGlyph(codepoint, options);
            glyph.ShouldNotBeNull($"U+{codepoint:X4}");
            var coverage = native.RasterizeGlyph(codepoint, coverageOptions)!;
            glyph.Width.ShouldBe(coverage.Width + 12, $"U+{codepoint:X4} is not padded by the spread");
            glyph.Height.ShouldBe(coverage.Height + 12, $"U+{codepoint:X4} is not padded by the spread");
            glyph.Format.ShouldBe(mode == SdfMode.Sdf ? PixelFormat.Grayscale8 : PixelFormat.Rgba32);
            glyph.BitmapData.ShouldContain(b => b >= 128, $"U+{codepoint:X4} has no inside texel");
            native.GetGlyphMetrics(codepoint, options).ShouldBe(glyph.Metrics, $"U+{codepoint:X4}");
        }
    }

    [Theory]
    [InlineData('A', 48f)]
    [InlineData('g', 48f)]
    [InlineData('&', 32f)]
    [InlineData('O', 96f)]
    public void CffSdf_ThresholdAt128_ReproducesTheCoverageBitmap(char c, float size)
    {
        // Wherever CFF coverage is unambiguous (well clear of 50%), the field's sign must agree,
        // and the field is the coverage box padded by the spread on every side.
        using var native = Load(new NativeRasterizer());
        var coverage = native.RasterizeGlyph(c, new RasterOptions { Size = size, EnableHinting = false })!;
        var field = native.RasterizeGlyph(c, new RasterOptions { Size = size, EnableHinting = false, Sdf = true, SdfSpread = 8 })!;

        int dx = coverage.Metrics.BearingX - field.Metrics.BearingX;
        int dy = field.Metrics.BearingY - coverage.Metrics.BearingY;
        dx.ShouldBe(8);
        dy.ShouldBe(8);

        int disagreements = 0;
        for (int y = 0; y < coverage.Height; y++)
        {
            for (int x = 0; x < coverage.Width; x++)
            {
                int cov = coverage.BitmapData[(y * coverage.Pitch) + x];
                if (cov is > 64 and < 192)
                    continue;

                bool inside = field.BitmapData[((y + dy) * field.Width) + x + dx] >= 128;
                if (inside != (cov >= 192))
                    disagreements++;
            }
        }

        disagreements.ShouldBe(0);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static RasterOptions Options(float size, bool bold, bool italic) =>
        new() { Size = size, Dpi = 72, EnableHinting = false, Bold = bold, Italic = italic };

    private static IRasterizer Load(IRasterizer rasterizer)
    {
        rasterizer.LoadFont(TestFonts.SourceSerifOtfBytes());
        return rasterizer;
    }

    private static long Ink(RasterizedGlyph glyph)
    {
        long sum = 0;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
                sum += glyph.BitmapData[(y * glyph.Pitch) + x];
        }

        return sum;
    }

    /// <summary>
    /// How far right the ink centre moves per pixel up, between rows a quarter of the way in
    /// from the glyph's top and bottom.
    /// </summary>
    private static double Lean(RasterizedGlyph glyph)
    {
        int top = -1, bottom = -1;
        for (int y = 0; y < glyph.Height; y++)
        {
            if (Centre(glyph, y) is null)
                continue;

            if (top < 0)
                top = y;
            bottom = y;
        }

        top.ShouldBeGreaterThanOrEqualTo(0, "glyph is blank");

        int quarter = (bottom - top) / 4;
        int upper = top + quarter, lower = bottom - quarter;
        return (Centre(glyph, upper)!.Value - Centre(glyph, lower)!.Value) / (lower - upper);
    }

    private static double? Centre(RasterizedGlyph glyph, int y)
    {
        long weight = 0, moment = 0;
        for (int x = 0; x < glyph.Width; x++)
        {
            int v = glyph.BitmapData[(y * glyph.Pitch) + x];
            weight += v;
            moment += (long)v * x;
        }

        return weight == 0 ? null : (double)moment / weight;
    }
}
