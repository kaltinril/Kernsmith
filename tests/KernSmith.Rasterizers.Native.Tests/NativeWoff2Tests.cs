using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 178 — core decodes WOFF2 (managed Brotli + glyf/hmtx transforms) before any backend
/// sees the bytes, so Native must render a WOFF2 exactly like the TTF it was made from.
/// The fixtures cover composite glyphs, hinted outlines, OVERLAP_SIMPLE flags, both loca
/// formats, the hmtx transform, and a CFF font (whose outline table WOFF2 stores untransformed).
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

    [Fact]
    public void Generate_FromCffWoff2_MatchesTheSourceOtf()
    {
        // WOFF2 carries a CFF table untransformed, so the decoded font is the .otf byte for byte
        // and Native's CFF decoder must render it identically.
        var fromOtf = BmFont.Generate(Fixture("NotoSans-Regular-Latin-CFF.otf"), Options());
        var fromWoff2 = BmFont.Generate(Fixture("NotoSans-Regular-Latin-CFF.woff2"), Options());

        fromOtf.Model.Characters.Count.ShouldBeGreaterThan(Text.Length - 2, "Native should render the test text");
        fromOtf.Pages[0].PixelData.ShouldContain(b => b != 0);

        fromWoff2.FntText.ShouldBe(fromOtf.FntText, "Native .fnt from the CFF WOFF2 should match the OTF");
        fromWoff2.Pages.Count.ShouldBe(fromOtf.Pages.Count);
        for (var i = 0; i < fromOtf.Pages.Count; i++)
            fromWoff2.Pages[i].PixelData.ShouldBe(fromOtf.Pages[i].PixelData, $"page {i} pixels");
    }
}
