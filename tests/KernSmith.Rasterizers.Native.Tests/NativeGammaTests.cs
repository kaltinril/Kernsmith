using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="RasterOptions.Gamma"/> on Native: the FreeType backend's contract — 1.8 is the
/// identity, and any other value maps grayscale coverage through <c>(c/255)^(1.8/gamma)</c> —
/// so a config renders the same tone on either backend. Mono, LCD and distance fields are
/// untouched, as on FreeType.
/// </summary>
public class NativeGammaTests
{
    private const string Sample = "aegos08BRS@&%";

    private static RasterOptions Options(float gamma, AntiAliasMode mode = AntiAliasMode.Grayscale, HintLevel level = HintLevel.Default) =>
        new() { Size = 11f, AntiAlias = mode, Gamma = gamma, HintLevel = level };

    [Fact]
    public void DefaultGamma_IsByteIdenticalToTheOldOutput()
    {
        using var native = Load();
        foreach (char c in Sample)
        {
            var at18 = native.RasterizeGlyph(c, Options(1.8f))!;
            var plain = native.RasterizeGlyph(c, new RasterOptions { Size = 11f })!;
            at18.BitmapData.ShouldBe(plain.BitmapData, $"'{c}'");
        }
    }

    [Theory]
    [InlineData(2.2f)]
    [InlineData(1.0f)]
    [InlineData(1.4f)]
    public void Gamma_MapsCoverageThroughTheFreeTypeCurve(float gamma)
    {
        // out = round(255 * (in/255)^(1.8/gamma)), 0 and 255 fixed — the FreeType backend's LUT.
        using var native = Load();
        double exponent = 1.8 / gamma;
        int changed = 0;
        foreach (char c in Sample)
        {
            var reference = native.RasterizeGlyph(c, Options(1.8f))!;
            var corrected = native.RasterizeGlyph(c, Options(gamma))!;
            corrected.Width.ShouldBe(reference.Width);
            corrected.Height.ShouldBe(reference.Height);
            for (int i = 0; i < reference.BitmapData.Length; i++)
            {
                byte expected = reference.BitmapData[i] is 0 or 255
                    ? reference.BitmapData[i]
                    : (byte)Math.Clamp((int)Math.Round(Math.Pow(reference.BitmapData[i] / 255.0, exponent) * 255.0, MidpointRounding.AwayFromZero), 0, 255);
                corrected.BitmapData[i].ShouldBe(expected, $"'{c}' pixel {i}: {reference.BitmapData[i]} at gamma {gamma}");
                changed += expected != reference.BitmapData[i] ? 1 : 0;
            }
        }

        changed.ShouldBeGreaterThan(0, "the sample has partially covered pixels, so a non-default gamma must change some");
    }

    [Fact]
    public void Gamma_AppliesToHintedGrayscaleToo()
    {
        using var native = Load();
        var light = native.RasterizeGlyph('e', Options(2.2f, AntiAliasMode.Light))!;
        var lightPlain = native.RasterizeGlyph('e', Options(1.8f, AntiAliasMode.Light))!;
        light.BitmapData.ShouldNotBe(lightPlain.BitmapData);

        var normal = native.RasterizeGlyph('e', Options(2.2f, level: HintLevel.Normal))!;
        var normalPlain = native.RasterizeGlyph('e', Options(1.8f, level: HintLevel.Normal))!;
        normal.BitmapData.ShouldNotBe(normalPlain.BitmapData);
    }

    [Fact]
    public void Gamma_LeavesMonoAlone()
    {
        using var native = Load();
        foreach (char c in Sample)
        {
            var corrected = native.RasterizeGlyph(c, Options(2.2f, AntiAliasMode.None))!;
            var plain = native.RasterizeGlyph(c, Options(1.8f, AntiAliasMode.None))!;
            corrected.BitmapData.ShouldBe(plain.BitmapData, $"'{c}'");
            corrected.BitmapData.ShouldAllBe(b => b == 0 || b == 255);
        }
    }

    [Fact]
    public void Gamma_LeavesDistanceFieldsAndLcdAlone()
    {
        using var native = Load();
        var sdf = native.RasterizeGlyph('e', new RasterOptions { Size = 11f, Sdf = true, Gamma = 2.2f })!;
        var sdfPlain = native.RasterizeGlyph('e', new RasterOptions { Size = 11f, Sdf = true })!;
        sdf.BitmapData.ShouldBe(sdfPlain.BitmapData);

        var lcd = native.RasterizeGlyph('e', Options(2.2f, AntiAliasMode.Lcd))!;
        var lcdPlain = native.RasterizeGlyph('e', Options(1.8f, AntiAliasMode.Lcd))!;
        lcd.BitmapData.ShouldBe(lcdPlain.BitmapData);
    }

    [Fact]
    public void InvalidGamma_IsTheIdentity()
    {
        using var native = Load();
        var zero = native.RasterizeGlyph('e', Options(0f))!;
        var negative = native.RasterizeGlyph('e', Options(-1f))!;
        var plain = native.RasterizeGlyph('e', Options(1.8f))!;
        zero.BitmapData.ShouldBe(plain.BitmapData);
        negative.BitmapData.ShouldBe(plain.BitmapData);
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
