using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 2 — drives real <c>.otf</c> fonts (CFF and CFF2 outlines) through the whole
/// pipeline on the native backend and checks it produces real, inked glyphs.
/// </summary>
/// <remarks>
/// The regression harness tolerates a backend failing a config, so these tests are what proves
/// Native actually renders PostScript outlines rather than throwing or drawing nothing.
/// </remarks>
public class NativeCffEndToEndTests
{
    public static TheoryData<string> OtfFixtures => ["SourceSerif4-Regular.otf", "SourceCodeVF-Upright.otf"];

    private static byte[] Load(string fixture) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

    private static FontGeneratorOptions Options(RasterizerBackend backend, bool hinting = true) => new()
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Backend = backend,
        EnableHinting = hinting
    };

    [Theory]
    [MemberData(nameof(OtfFixtures))]
    public void Generate_WithNativeBackend_ProducesInkedBmFont(string fixture)
    {
        var result = BmFont.Generate(Load(fixture), Options(RasterizerBackend.Native));

        var model = BmFontReader.ReadText(result.FntText);
        model.Characters.Count.ShouldBeGreaterThan(90, "ASCII should yield most of its printable glyphs");

        foreach (char c in "AOgx&8")
        {
            var cell = model.Characters.First(ch => ch.Id == c);
            cell.Width.ShouldBeGreaterThan(5, $"'{c}' width");
            cell.Height.ShouldBeGreaterThan(5, $"'{c}' height");
            cell.XAdvance.ShouldBeGreaterThan(0, $"'{c}' xadvance");
        }

        model.Characters.First(ch => ch.Id == ' ').XAdvance.ShouldBeGreaterThan(0);
        result.Pages[0].PixelData.ShouldContain(b => b != 0);
    }

    [Theory]
    [MemberData(nameof(OtfFixtures))]
    public void Generate_WithNativeBackend_MetricsTrackFreeType(string fixture)
    {
        var bytes = Load(fixture);

        // Unhinted on both sides: Native has no hinter, and FreeType's CFF hinter moves heights by
        // up to 2px at this size (Source Serif '0' is 15px hinted, 17px unhinted on both backends).
        var native = BmFontReader.ReadText(BmFont.Generate(bytes, Options(RasterizerBackend.Native, hinting: false)).FntText);
        var freeType = BmFontReader.ReadText(BmFont.Generate(bytes, Options(RasterizerBackend.FreeType, hinting: false)).FntText);

        ShouldBeWithinOne(native.Common.LineHeight, freeType.Common.LineHeight, "lineHeight");
        ShouldBeWithinOne(native.Common.Base, freeType.Common.Base, "base");

        foreach (var expected in freeType.Characters)
        {
            var actual = native.Characters.FirstOrDefault(c => c.Id == expected.Id);
            actual.ShouldNotBeNull($"native is missing U+{expected.Id:X4}");
            ShouldBeWithinOne(actual!.XAdvance, expected.XAdvance, $"U+{expected.Id:X4} xadvance");
            ShouldBeWithinOne(actual.Width, expected.Width, $"U+{expected.Id:X4} width");
            ShouldBeWithinOne(actual.Height, expected.Height, $"U+{expected.Id:X4} height");
        }
    }

    [Fact]
    public void ColrLayers_DrawnWithCffGlyphs_RenderThroughTheCffDecoder()
    {
        // COLR layers are prepared through the face's own decoder, so a CFF font's layers are
        // charstring outlines: 'O' layered as itself in red must come out as the plain 'O'
        // coverage, colored.
        var plainFont = TestFonts.SourceSerifOtfBytes();
        int o = NativeFontFace.Load(plainFont).GetGlyphIndex('O');
        var colorFont = SyntheticFonts.WithTables(plainFont, new Dictionary<string, byte[]?>
        {
            ["COLR"] = SyntheticColorTables.Colr((o, [new SyntheticColorTables.Layer(o, 0)])),
            ["CPAL"] = SyntheticColorTables.Cpal([[0xFF0000FFu]]),
        });

        using var plain = new NativeRasterizer();
        plain.LoadFont(plainFont);
        using var color = new NativeRasterizer();
        color.LoadFont(colorFont);

        var coverage = plain.RasterizeGlyph('O', new RasterOptions { Size = 32 })!;
        var colored = color.RasterizeGlyph('O', new RasterOptions { Size = 32, ColorFont = true })!;

        colored.Format.ShouldBe(PixelFormat.Rgba32);
        colored.Width.ShouldBe(coverage.Width);
        colored.Height.ShouldBe(coverage.Height);
        for (int p = 0; p < coverage.Width * coverage.Height; p++)
        {
            colored.BitmapData[(p * 4) + 3].ShouldBe(coverage.BitmapData[p], $"alpha at pixel {p}");
            if (coverage.BitmapData[p] > 0)
                (colored.BitmapData[p * 4], colored.BitmapData[(p * 4) + 1], colored.BitmapData[(p * 4) + 2]).ShouldBe(((byte)255, (byte)0, (byte)0));
        }
    }

    private static void ShouldBeWithinOne(int actual, int expected, string what) =>
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(1,
            $"{what}: native {actual} vs FreeType {expected}");
}
