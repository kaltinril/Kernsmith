using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Light hinting moves ink by at most one pixel: the auto-hinter snaps edges to the grid, so a
/// glyph's top and bottom ink rows may shift by a row, never more. Features that are not near a
/// blue zone, and hairline curves that are not strokes, must not be dragged several pixels.
/// </summary>
/// <remarks>
/// The cases are glyphs a Native-vs-FreeType sweep found moving 2-4 px where FreeType's light
/// hinting moves them by at most one: a swash tail, a tilde, an accent, the feet of a currency
/// sign and a below-baseline swash.
/// </remarks>
public class LightHintingReachTests
{
    public static TheoryData<string, string, float, char> ReportedGlyphs => new()
    {
        // The tail's hairline curves were inflated into stacked one-pixel strokes: bottom -1 -> -4.
        { "DancingScript-Variable.ttf", "default", 8f, 'Q' },
        // The tilde's two humps were stacked above the cap-height zone: top 7 -> 10.
        { "SourceSerif4-Regular.otf", "default", 8f, 'Ã' },
        // The ring snapped into the x-height zone and widened to the standard stroke: bottom 4 -> 0.
        { "SourceCodeVF-Upright.otf", "wght=900", 72f, '¤' },
        // The accent's top was captured by the ascender zone 1.3 px below it: top 55 -> 53.
        { "SourceSerif4-Regular.otf", "default", 72f, 'é' },
        // The swash's stroke shrank to the rounded standard width and lifted: bottom -5 -> -3.
        { "DancingScript-Variable.ttf", "default", 48f, 'C' },
    };

    public static TheoryData<string, string> Fonts => new()
    {
        { "Roboto-Regular.ttf", "default" },
        { "NotoSans-Regular-Latin.ttf", "default" },
        { "NotoSans-Regular-Latin-CFF.otf", "default" },
        { "SourceSerif4-Regular.otf", "default" },
        { "RobotoFlex-Variable.ttf", "default" },
        { "RobotoFlex-Variable.ttf", "wght=1000" },
        { "SourceCodeVF-Upright.otf", "default" },
        { "SourceCodeVF-Upright.otf", "wght=900" },
        { "DancingScript-Variable.ttf", "default" },
        { "DancingScript-Variable.ttf", "wght=700" },
        { Path.Combine("Color", "BungeeColor-Regular.ttf"), "default" },
        { Path.Combine("Color", "ReemKufiInk-Regular.ttf"), "default" },
    };

    private static readonly float[] Sizes = [8f, 12f, 16f, 24f, 32f, 48f, 72f];

    [Theory]
    [MemberData(nameof(ReportedGlyphs))]
    public void ReportedGlyph_InkMovesAtMostOnePixel(string font, string axes, float size, char character)
    {
        using var native = Load(font, axes);

        var hinted = Ink(native.RasterizeGlyph(character, Options(size, hinting: true))!);
        var unhinted = Ink(native.RasterizeGlyph(character, Options(size, hinting: false))!);

        hinted.Top.ShouldBeInRange(unhinted.Top - 1, unhinted.Top + 1, "ink top");
        hinted.Bottom.ShouldBeInRange(unhinted.Bottom - 1, unhinted.Bottom + 1, "ink bottom");
    }

    [Theory]
    [MemberData(nameof(Fonts))]
    public void EveryLatinGlyph_InkMovesAtMostOnePixel(string font, string axes)
    {
        using var native = Load(font, axes);
        var failures = new StringBuilder();

        foreach (float size in Sizes)
        {
            foreach (int codepoint in Enumerable.Range(0x21, 0x7F - 0x21).Concat(Enumerable.Range(0xA1, 0x5F)))
            {
                var hintedGlyph = native.RasterizeGlyph(codepoint, Options(size, hinting: true));
                var unhintedGlyph = native.RasterizeGlyph(codepoint, Options(size, hinting: false));
                if (hintedGlyph is null || unhintedGlyph is null)
                    continue;

                var hinted = Ink(hintedGlyph);
                var unhinted = Ink(unhintedGlyph);
                if (hinted.Empty || unhinted.Empty)
                    continue;

                if (Math.Abs(hinted.Top - unhinted.Top) > 1 || Math.Abs(hinted.Bottom - unhinted.Bottom) > 1)
                {
                    failures.Append(CultureInfo.InvariantCulture,
                        $"{size}px U+{codepoint:X4} '{(char)codepoint}': ink {unhinted.Bottom}..{unhinted.Top} -> {hinted.Bottom}..{hinted.Top}\n");
                }
            }
        }

        failures.ToString().ShouldBeEmpty($"{font} {axes}: Light hinting moved ink by more than one pixel");
    }

    private static RasterOptions Options(float size, bool hinting) =>
        new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = hinting };

    private static NativeRasterizer Load(string font, string axes)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", font));
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (axes != "default")
        {
            var values = axes.Split(',').Select(a => a.Split('='))
                .ToDictionary(a => a[0], a => float.Parse(a[1], CultureInfo.InvariantCulture));
            native.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, values);
        }

        return native;
    }

    /// <summary>The topmost and bottommost inked rows, pen-relative, Y up (Top is exclusive).</summary>
    private static (bool Empty, int Top, int Bottom) Ink(RasterizedGlyph glyph)
    {
        int bytesPerPixel = glyph.Format == PixelFormat.Rgba32 ? 4 : 1;
        int alpha = bytesPerPixel == 4 ? 3 : 0;
        int first = -1, last = -1;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + (x * bytesPerPixel) + alpha] == 0)
                    continue;
                if (first < 0)
                    first = y;
                last = y;
                break;
            }
        }

        return first < 0 ? (true, 0, 0) : (false, glyph.Metrics.BearingY - first, glyph.Metrics.BearingY - last - 1);
    }
}
