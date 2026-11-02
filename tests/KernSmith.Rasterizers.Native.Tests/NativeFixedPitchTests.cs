using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Fixed-pitch (monospaced) faces under <see cref="HintLevel.Normal"/> and the default: every
/// glyph, the space included, keeps the rounded linear advance in both anti-alias modes, the
/// ink stays in its cell, and the kerning bake emits nothing. A face that forgets
/// <c>post.isFixedPitch</c> is recognised from its advances.
/// </summary>
public class NativeFixedPitchTests
{
    private const string Sample = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    public static TheoryData<string, float, AntiAliasMode> Cases
    {
        get
        {
            var data = new TheoryData<string, float, AntiAliasMode>();
            foreach (string font in new[] { "SourceCodeVF", "Consolas" })
            {
                foreach (float size in new[] { 8f, 10f, 12f, 16f })
                {
                    data.Add(font, size, AntiAliasMode.Grayscale);
                    data.Add(font, size, AntiAliasMode.None);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryAdvance_IsTheRoundedLinearOne_AndTheInkStaysInItsCell(string font, float size, AntiAliasMode mode)
    {
        using var native = Load(font);
        var failures = new StringBuilder();
        foreach (var level in new[] { HintLevel.Normal, HintLevel.Default })
        {
            var hinted = new RasterOptions { Size = size, AntiAlias = mode, HintLevel = level };
            var unhinted = new RasterOptions { Size = size, AntiAlias = mode, HintLevel = HintLevel.None };
            foreach (char c in Sample)
            {
                var h = native.RasterizeGlyph(c, hinted)!;
                var u = native.RasterizeGlyph(c, unhinted)!;
                if (h.Metrics.Advance != u.Metrics.Advance)
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}' {level} advance {h.Metrics.Advance} vs {u.Metrics.Advance}; ");
                // The ink stays in its cell, a pixel of overhang allowed where the font itself
                // overhangs: a block of stems set in these cells ("||||", "llll") stays in
                // columns at the pitch.
                int lowest = Math.Min(-1, u.Metrics.BearingX), highest = Math.Max(u.Metrics.Advance + 1, u.Metrics.BearingX + u.Width);
                if (h.Width > 0 && (h.Metrics.BearingX < lowest || h.Metrics.BearingX + h.Width > highest))
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}' {level} ink {h.Metrics.BearingX}..{h.Metrics.BearingX + h.Width} outside its {h.Metrics.Advance} px cell; ");
                native.GetGlyphMetrics(c, hinted)!.Value.ShouldBe(h.Metrics, $"'{c}' {level} metrics");
            }
        }

        failures.ToString().ShouldBeEmpty($"{font} {size}px {mode}");
    }

    [Theory]
    [InlineData("SourceCodeVF")]
    [InlineData("Consolas")]
    public void TheBake_EmitsNoPairs(string font)
    {
        using var native = Load(font);
        foreach (var mode in new[] { AntiAliasMode.Grayscale, AntiAliasMode.None })
        {
            var options = new RasterOptions { Size = 12f, AntiAlias = mode, HintLevel = HintLevel.Normal, BakeHintingIntoKerning = true };
            native.RasterizeAll(Sample.Select(c => (int)c).ToList(), options);
            native.GetKerningAdjustments(options).ShouldBeNull($"{font} {mode}");
        }
    }

    [Fact]
    public void IsFixedPitch_TakesTheFlag_OrNinetyFivePercentOfOneAdvance()
    {
        GlyphHinter.IsFixedPitch(flagged: true, []).ShouldBeTrue();
        GlyphHinter.IsFixedPitch(flagged: false, []).ShouldBeFalse();
        GlyphHinter.IsFixedPitch(flagged: false, Enumerable.Repeat(600, 95).Concat(Enumerable.Repeat(1200, 5))).ShouldBeTrue();
        GlyphHinter.IsFixedPitch(flagged: false, Enumerable.Repeat(600, 94).Concat(Enumerable.Repeat(1200, 6))).ShouldBeFalse();
        GlyphHinter.IsFixedPitch(flagged: false, [500, 600, 700, 600]).ShouldBeFalse();
    }

    private static NativeRasterizer Load(string font)
    {
        byte[] bytes;
        if (font == "SourceCodeVF")
        {
            bytes = File.ReadAllBytes(TestFonts.SourceCodeVariableOtfPath);
        }
        else
        {
            var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
            Assert.SkipUnless(File.Exists(path), "Consolas is not installed on this machine.");
            bytes = File.ReadAllBytes(path);
        }

        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        return native;
    }
}
