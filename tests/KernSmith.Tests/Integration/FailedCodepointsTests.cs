using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// <see cref="Output.BmFontResult.FailedCodepoints"/> lists every requested character the
/// result lacks — those the font has no glyph for included, as an incremental session reports.
/// </summary>
public sealed class FailedCodepointsTests
{
    private const int PrivateUse = 0xE000; // Roboto maps nothing here.

    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static FontGeneratorOptions Options() => new()
    {
        Size = 24,
        Characters = CharacterSet.FromChars(new[] { (int)'A', PrivateUse, 'B' }),
        Rasterizer = new StbTrueTypeRasterizer(),
    };

    [Fact]
    public void Generate_ReportsACharacterMissingFromTheFont()
    {
        var result = BmFont.Generate(Font(), Options());

        result.FailedCodepoints.ShouldBe(new[] { PrivateUse });
        result.Model.Characters.Select(c => c.Id).OrderBy(id => id).ShouldBe(new[] { (int)'A', 'B' });
    }

    [Fact]
    public void GenerateBatch_Combined_ReportsACharacterMissingFromTheFont()
    {
        var job = new BatchJob { FontData = Font(), Options = Options() };
        var batch = BmFont.GenerateBatch([job], new BatchOptions { AtlasMode = BatchAtlasMode.Combined });

        batch.Results.Single().Result!.FailedCodepoints.ShouldBe(new[] { PrivateUse });
    }

    [Fact]
    public void Generate_FontCoveringEveryCharacter_ReportsNone()
    {
        var options = Options();
        options.Characters = CharacterSet.FromChars("AB");

        BmFont.Generate(Font(), options).FailedCodepoints.ShouldBeEmpty();
    }
}
