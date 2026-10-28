using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 178 — core decodes WOFF2 (managed Brotli + glyf/hmtx transforms) before any backend
/// sees the bytes, so Native must render a WOFF2 exactly like the TTF it was made from.
/// The fixtures cover composite glyphs, hinted outlines, OVERLAP_SIMPLE flags, both loca
/// formats and the hmtx transform.
/// </summary>
public class NativeWoff2Tests
{
    private const string Text = "AWTVaeogy.,!?0123ÀÉÑçöÅÿ";

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static FontGeneratorOptions Options() => new()
    {
        Size = 28,
        Characters = CharacterSet.FromChars(Text),
        Backend = RasterizerBackend.Native,
    };

    [Theory]
    [InlineData("Roboto-Regular")]
    [InlineData("NotoSans-Regular-Latin")]
    [InlineData("RobotoFlex-Latin")]
    public void Generate_FromWoff2_MatchesTheSourceTtf(string fixture)
    {
        var fromTtf = BmFont.Generate(Fixture(fixture + ".ttf"), Options());
        var fromWoff2 = BmFont.Generate(Fixture(fixture + ".woff2"), Options());

        fromTtf.Model.Characters.Count.ShouldBeGreaterThan(Text.Length - 2, "Native should render the test text");
        fromTtf.Pages[0].PixelData.ShouldContain(b => b != 0);

        fromWoff2.FntText.ShouldBe(fromTtf.FntText, $"{fixture}: Native .fnt from WOFF2 should match the TTF");
        fromWoff2.Pages.Count.ShouldBe(fromTtf.Pages.Count);
        for (var i = 0; i < fromTtf.Pages.Count; i++)
            fromWoff2.Pages[i].PixelData.ShouldBe(fromTtf.Pages[i].PixelData, $"{fixture}: page {i} pixels");
    }
}
