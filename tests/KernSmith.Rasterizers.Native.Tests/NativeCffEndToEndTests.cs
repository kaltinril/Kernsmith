using KernSmith.Output;
using KernSmith.Rasterizer;
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

    private static void ShouldBeWithinOne(int actual, int expected, string what) =>
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(1,
            $"{what}: native {actual} vs FreeType {expected}");
}
