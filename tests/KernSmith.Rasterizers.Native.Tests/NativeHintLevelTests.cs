using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 188 — <see cref="HintLevel"/> on Native. An explicit level owns hinting:
/// <see cref="HintLevel.Light"/> is the Phase 174 y-only fitter whatever the anti-alias mode,
/// <see cref="HintLevel.None"/> is unhinted, and <see cref="HintLevel.Default"/> is exactly the
/// old reading of <see cref="RasterOptions.EnableHinting"/> + <see cref="AntiAliasMode.Light"/>.
/// <see cref="HintLevel.Normal"/> is the both-axis auto-hinter (its own tests are
/// <c>NativeNormalHintingTests</c>); <see cref="HintLevel.Full"/> is not advertised yet and
/// renders as Normal when handed to the rasterizer directly.
/// </summary>
public class NativeHintLevelTests
{
    private const string GlyphSample = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()[]{}<>@#$%&*-+=/\\|~^";

    public static TheoryData<float> Sizes => [10f, 12f, 14f, 16f, 24f];

    [Fact]
    public void Capabilities_AdvertiseNoneLightAndNormal()
    {
        var levels = new NativeRasterizer().Capabilities.SupportedHintLevels;
        levels.ShouldBe([HintLevel.None, HintLevel.Light, HintLevel.Normal], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void LightLevel_IsExactlyTheOldLightPlusHintingOutput(float size)
    {
        using var native = Load();
        var level = new RasterOptions { Size = size, HintLevel = HintLevel.Light };
        var old = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = true };
        var unhinted = new RasterOptions { Size = size, EnableHinting = false };

        int hintedGlyphs = 0;
        foreach (char c in GlyphSample)
        {
            var a = native.RasterizeGlyph(c, level)!;
            var b = native.RasterizeGlyph(c, old)!;
            a.BitmapData.ShouldBe(b.BitmapData, $"'{c}' at {size}px");
            a.Metrics.ShouldBe(b.Metrics, $"'{c}' at {size}px");
            native.GetGlyphMetrics(c, level).ShouldBe(a.Metrics, $"'{c}' metrics at {size}px follow the hinted bitmap");

            if (!a.BitmapData.AsSpan().SequenceEqual(native.RasterizeGlyph(c, unhinted)!.BitmapData))
                hintedGlyphs++;
        }

        hintedGlyphs.ShouldBeGreaterThan(0, "the level really hints: some glyph differs from the unhinted render");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void LightLevel_HintsGrayscaleAndMono_TheSameWayLightDid(float size)
    {
        // The level owns hinting, so AntiAlias no longer gates it: Grayscale + Light level is the
        // hinted output, and None (mono) + Light level thresholds the hinted outline.
        using var native = Load();
        var grayLevel = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Grayscale, HintLevel = HintLevel.Light };
        var lightAa = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Light, HintLevel = HintLevel.Light };
        var monoLevel = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, HintLevel = HintLevel.Light };
        var monoUnhinted = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.None, HintLevel = HintLevel.None };

        int hintedMono = 0;
        foreach (char c in GlyphSample)
        {
            native.RasterizeGlyph(c, grayLevel)!.BitmapData.ShouldBe(native.RasterizeGlyph(c, lightAa)!.BitmapData, $"'{c}'");

            var mono = native.RasterizeGlyph(c, monoLevel)!;
            mono.BitmapData.ShouldAllBe(v => v == 0 || v == 255, $"'{c}' mono stays thresholded");
            native.GetGlyphMetrics(c, monoLevel).ShouldBe(mono.Metrics, $"'{c}' mono metrics at {size}px");
            if (!mono.BitmapData.AsSpan().SequenceEqual(native.RasterizeGlyph(c, monoUnhinted)!.BitmapData))
                hintedMono++;
        }

        hintedMono.ShouldBeGreaterThan(0, "mono at an explicit Light level is hinted");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void NoneLevel_IsExactlyHintingOff(float size)
    {
        using var native = Load();
        foreach (var aa in new[] { AntiAliasMode.Grayscale, AntiAliasMode.Light, AntiAliasMode.None })
        {
            var level = new RasterOptions { Size = size, AntiAlias = aa, HintLevel = HintLevel.None };
            var off = new RasterOptions { Size = size, AntiAlias = aa, EnableHinting = false };

            foreach (char c in GlyphSample)
            {
                var a = native.RasterizeGlyph(c, level)!;
                var b = native.RasterizeGlyph(c, off)!;
                a.BitmapData.ShouldBe(b.BitmapData, $"'{c}' {aa} at {size}px");
                a.Metrics.ShouldBe(b.Metrics, $"'{c}' {aa} at {size}px");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void DefaultLevel_KeepsTheOldMapping(float size)
    {
        // Grayscale with hinting on stays unhinted (default output does not move); Light with
        // hinting on stays hinted; anything with hinting off stays unhinted.
        using var native = Load();
        foreach (char c in GlyphSample)
        {
            native.RasterizeGlyph(c, new RasterOptions { Size = size })!.BitmapData
                .ShouldBe(native.RasterizeGlyph(c, new RasterOptions { Size = size, HintLevel = HintLevel.None })!.BitmapData, $"'{c}' default grayscale");
            native.RasterizeGlyph(c, new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Light })!.BitmapData
                .ShouldBe(native.RasterizeGlyph(c, new RasterOptions { Size = size, HintLevel = HintLevel.Light })!.BitmapData, $"'{c}' default light");
            native.RasterizeGlyph(c, new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = false })!.BitmapData
                .ShouldBe(native.RasterizeGlyph(c, new RasterOptions { Size = size, HintLevel = HintLevel.None })!.BitmapData, $"'{c}' default light, hinting off");
        }
    }

    [Fact]
    public void Normal_IsNotLight()
    {
        // Normal fits x as well as y: the stems of 'h' land in whole columns, which Light's
        // y-only fit leaves straddling two, so the bitmaps differ.
        using var native = Load();
        int differing = 0;
        foreach (char c in GlyphSample)
        {
            var normal = native.RasterizeGlyph(c, new RasterOptions { Size = 12f, HintLevel = HintLevel.Normal })!;
            var light = native.RasterizeGlyph(c, new RasterOptions { Size = 12f, HintLevel = HintLevel.Light })!;
            if (!normal.BitmapData.AsSpan().SequenceEqual(light.BitmapData) || normal.Metrics != light.Metrics)
                differing++;
        }

        differing.ShouldBeGreaterThan(GlyphSample.Length / 2);
    }

    [Fact]
    public void Full_RendersAsNormal_UntilAFontNativeEngineLands()
    {
        // Full (the font's own hinting) has no engine yet; a direct Full request runs the
        // Normal auto-hinter, and core steps Full down to Normal because Full is not advertised.
        using var native = Load();
        foreach (char c in GlyphSample)
        {
            var full = native.RasterizeGlyph(c, new RasterOptions { Size = 12f, HintLevel = HintLevel.Full })!;
            var normal = native.RasterizeGlyph(c, new RasterOptions { Size = 12f, HintLevel = HintLevel.Normal })!;
            full.BitmapData.ShouldBe(normal.BitmapData, $"'{c}'");
            full.Metrics.ShouldBe(normal.Metrics, $"'{c}'");
        }
    }

    [Fact]
    public void DistanceFieldsAndLcd_StayUnhintedAtEveryLevel()
    {
        using var native = Load();
        foreach (var level in Enum.GetValues<HintLevel>())
        {
            var sdf = native.RasterizeGlyph('H', new RasterOptions { Size = 14f, Sdf = true, HintLevel = level })!;
            var sdfOff = native.RasterizeGlyph('H', new RasterOptions { Size = 14f, Sdf = true, EnableHinting = false })!;
            sdf.BitmapData.ShouldBe(sdfOff.BitmapData, $"SDF at {level}");

            var lcd = native.RasterizeGlyph('H', new RasterOptions { Size = 14f, AntiAlias = AntiAliasMode.Lcd, HintLevel = level })!;
            var lcdOff = native.RasterizeGlyph('H', new RasterOptions { Size = 14f, AntiAlias = AntiAliasMode.Lcd, EnableHinting = false })!;
            lcd.BitmapData.ShouldBe(lcdOff.BitmapData, $"LCD at {level}");
        }
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
