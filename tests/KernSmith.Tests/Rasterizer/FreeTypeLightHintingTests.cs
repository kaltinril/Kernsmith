using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// The FreeType backend's <see cref="AntiAliasMode.Light"/> (and <see cref="HintLevel.Light"/>)
/// loads the glyph with FreeType's light hinting target. Light hinting is a <em>load</em> target,
/// not a render mode: rendering a normally hinted outline in <c>FT_RENDER_MODE_LIGHT</c> gives the
/// same coverage as <c>FT_RENDER_MODE_NORMAL</c>, which left <c>Light</c> byte-identical to
/// <c>Grayscale</c>.
/// </summary>
public sealed class FreeTypeLightHintingTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static RasterOptions Options(AntiAliasMode aa, bool hinting = true, HintLevel level = HintLevel.Default) =>
        new() { Size = 12, AntiAlias = aa, EnableHinting = hinting, HintLevel = level };

    private static IEnumerable<int> Ascii => Enumerable.Range('!', '~' - '!' + 1);

    [Fact]
    public void Light_IsNotGrayscale_AtRoboto12()
    {
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        var differing = Ascii.Count(c =>
        {
            var light = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light))!;
            var gray = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale))!;
            return light.Metrics != gray.Metrics || !light.BitmapData.SequenceEqual(gray.BitmapData);
        });

        differing.ShouldBeGreaterThan(0, "Light rendered byte-identical to Grayscale: the light target was not applied at load");
    }

    [Fact]
    public void Light_KeepsTheUnhintedAdvances_WhereNormalHintingRebuildsThem()
    {
        // FreeType's light hinting fits the y axis only, so the advance is the linear advance
        // rounded, as without hinting. The normal auto-hinter (Grayscale on Roboto, which has no
        // bytecode) rebuilds advances from the fitted stems and grows some by a pixel.
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        var failures = new List<string>();
        var rebuilt = 0;
        foreach (var c in Ascii)
        {
            var unhinted = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale, hinting: false))!.Metrics.Advance;
            var light = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light))!.Metrics.Advance;
            var normal = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale))!.Metrics.Advance;
            if (light != unhinted)
                failures.Add($"'{(char)c}': light {light}, unhinted {unhinted}");
            if (normal != unhinted)
                rebuilt++;
        }

        failures.ShouldBeEmpty();
        rebuilt.ShouldBeGreaterThan(0, "normal hinting leaves every Roboto advance alone at 12px, so this proves nothing");
    }

    [Fact]
    public void HintLevelLight_RendersAsAntiAliasLight()
    {
        // An explicit HintLevel.Light with Grayscale rendering is the same light-hinted glyph.
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        foreach (var c in Ascii)
        {
            var byMode = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light))!;
            var byLevel = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale, level: HintLevel.Light))!;

            byLevel.Metrics.ShouldBe(byMode.Metrics, $"'{(char)c}'");
            byLevel.BitmapData.ShouldBe(byMode.BitmapData, $"'{(char)c}'");
        }
    }

    [Fact]
    public void ExplicitFull_WithAntiAliasLight_KeepsTheDefaultTarget()
    {
        // HintLevel.Full is FreeType's own engine choice; Light anti-aliasing does not override it.
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        foreach (var c in Ascii)
        {
            var full = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light, level: HintLevel.Full))!;
            var gray = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale))!;

            full.Metrics.ShouldBe(gray.Metrics, $"'{(char)c}'");
            full.BitmapData.ShouldBe(gray.BitmapData, $"'{(char)c}'");
        }
    }

    [Fact]
    public void Light_GlyphMetricsTakeTheLightTargetToo()
    {
        // GetGlyphMetrics loads the glyph without rendering; it must load with the same target or
        // it reports the normal-hinted advance for a light-hinted bitmap.
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        foreach (var c in Ascii)
        {
            var rendered = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light))!.Metrics;
            var measured = ft.GetGlyphMetrics(c, Options(AntiAliasMode.Light))!.Value;

            measured.Advance.ShouldBe(rendered.Advance, $"'{(char)c}'");
        }
    }

    [Fact]
    public void Unhinted_Light_StaysUnhinted()
    {
        // EnableHinting = false still loads FT_LOAD_NO_HINTING whatever the anti-alias mode.
        using var ft = new FreeTypeRasterizer();
        ft.LoadFont(Roboto());

        foreach (var c in Ascii)
        {
            var light = ft.RasterizeGlyph(c, Options(AntiAliasMode.Light, hinting: false))!;
            var gray = ft.RasterizeGlyph(c, Options(AntiAliasMode.Grayscale, hinting: false))!;

            light.Metrics.ShouldBe(gray.Metrics, $"'{(char)c}'");
            light.BitmapData.ShouldBe(gray.BitmapData, $"'{(char)c}'");
        }
    }
}
