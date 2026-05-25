#if WINDOWS
using KernSmith.Rasterizers.Gdi;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Verifies that non-GDI backends produce the same base/lineHeight as GDI for a given font,
/// ensuring cross-backend baseline alignment matches BMFont's reference output (issue #67).
/// </summary>
[Collection("RasterizerFactory")]
public class BaselineAlignmentTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.DirectWrite)]
    public void Generate_BaseAndLineHeight_MatchGdi(RasterizerBackend backend)
    {
        var fontData = LoadTestFont();

        var gdiResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Backend = RasterizerBackend.Gdi,
            Characters = CharacterSet.Ascii,
        });
        var otherResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Backend = backend,
            Characters = CharacterSet.Ascii,
        });

        otherResult.Model.Common.Base.ShouldBe(
            gdiResult.Model.Common.Base,
            $"{backend} base should match GDI (issue #67)");

        otherResult.Model.Common.LineHeight.ShouldBe(
            gdiResult.Model.Common.LineHeight,
            $"{backend} lineHeight should match GDI");
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.DirectWrite)]
    public void Generate_EmHeightMode_BaseAndLineHeight_MatchFreeType(RasterizerBackend backend)
    {
        // Verify em-height mode (MatchCharHeight=true — the Gum integration path).
        // FreeType and DirectWrite should agree with each other; GDI has a known
        // em-height sizing limitation (bug tracked separately) so we only compare
        // non-GDI backends here.
        var fontData = LoadTestFont();

        var freetypeResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            MatchCharHeight = true,
            Backend = RasterizerBackend.FreeType,
            Characters = CharacterSet.Ascii,
        });
        var otherResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            MatchCharHeight = true,
            Backend = backend,
            Characters = CharacterSet.Ascii,
        });

        otherResult.Model.Common.Base.ShouldBe(
            freetypeResult.Model.Common.Base,
            $"{backend} base should match FreeType in em-height mode");

        otherResult.Model.Common.LineHeight.ShouldBe(
            freetypeResult.Model.Common.LineHeight,
            $"{backend} lineHeight should match FreeType in em-height mode");
    }
}
#endif
