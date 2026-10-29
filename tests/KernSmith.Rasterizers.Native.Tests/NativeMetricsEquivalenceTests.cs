using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <c>GetGlyphMetrics</c> must describe exactly the glyph <c>RasterizeGlyph</c> produces, for
/// every mapped codepoint of every fixture, in every render mode — including where metrics take
/// a cheaper route than rendering (the declared <c>glyf</c> box of an untransformed, unhinted
/// glyph), so a shortcut can never drift from the pipeline it stands in for.
/// </summary>
public class NativeMetricsEquivalenceTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (string font in new[] { "Roboto", "SourceSerif", "RobotoFlex", "RobotoFlexBlack", "Bungee" })
        {
            foreach (string mode in ModeNames)
                data.Add(font, mode);
        }

        return data;
    }

    private static readonly string[] ModeNames =
    [
        "Grayscale", "None", "Light", "LightHinted", "Lcd", "Sdf", "Msdf", "Bold", "Italic", "Color", "Dpi96",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void GetGlyphMetrics_EqualsTheRasterizedMetrics_ForEveryMappedCodepoint(string font, string mode)
    {
        var bytes = FontBytes(font);
        var codepoints = BmFont.ReadFontInfo(bytes).AvailableCodepoints.OrderBy(c => c).ToArray();
        codepoints.Length.ShouldBeGreaterThan(90, "the fixture's cmap should cover at least ASCII");

        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        if (font == "RobotoFlexBlack")
            native.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, new Dictionary<string, float> { ["wght"] = 1000f });

        var failures = new StringBuilder();
        int compared = 0;
        foreach (float size in Sizes(mode))
        {
            var options = Options(mode, size);
            foreach (int codepoint in codepoints)
            {
                var rendered = native.RasterizeGlyph(codepoint, options);
                var measured = native.GetGlyphMetrics(codepoint, options);
                compared++;

                if (rendered is null)
                {
                    if (measured is not null)
                        failures.Append(CultureInfo.InvariantCulture, $"U+{codepoint:X4} @{size}: measured {measured} but nothing rendered\n");
                    continue;
                }

                if (measured != rendered.Metrics)
                    failures.Append(CultureInfo.InvariantCulture, $"U+{codepoint:X4} @{size}: measured {measured}, rendered {rendered.Metrics}\n");
            }
        }

        compared.ShouldBeGreaterThan(codepoints.Length);
        failures.ToString().ShouldBeEmpty();
    }

    private static float[] Sizes(string mode) => mode switch
    {
        // Distance fields cost far more per glyph; two sizes still cover both rounding regimes.
        "Sdf" or "Msdf" => [9f, 32f],
        _ => [1f, 6f, 11f, 17.5f, 32f, 64f],
    };

    private static RasterOptions Options(string mode, float size) => mode switch
    {
        "Grayscale" => new() { Size = size },
        "None" => new() { Size = size, AntiAlias = AntiAliasMode.None },
        "Light" => new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = false },
        "LightHinted" => new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = true },
        "Lcd" => new() { Size = size, AntiAlias = AntiAliasMode.Lcd },
        "Sdf" => new() { Size = size, Sdf = true },
        "Msdf" => new() { Size = size, SdfMode = SdfMode.Msdf },
        "Bold" => new() { Size = size, Bold = true },
        "Italic" => new() { Size = size, Italic = true },
        "Color" => new() { Size = size, ColorFont = true },
        "Dpi96" => new() { Size = size, Dpi = 96 },
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static byte[] FontBytes(string font) => font switch
    {
        "Roboto" => TestFonts.RobotoRegularBytes(),
        "SourceSerif" => TestFonts.SourceSerifOtfBytes(),
        "RobotoFlex" or "RobotoFlexBlack" => TestFonts.RobotoFlexBytes(),
        "Bungee" => TestFonts.BungeeColorBytes(),
        _ => throw new ArgumentOutOfRangeException(nameof(font), font, null),
    };
}
