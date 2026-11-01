using KernSmith.Output;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// The <c>info</c> line records how the font was rendered: <c>aa</c> is the super-sample level
/// and <c>stretchH</c> the height percent, as BMFont writes them.
/// </summary>
public sealed class InfoBlockTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult Generate(int superSample, int heightPercent) => BmFont.Generate(Font(), new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.FromChars("AB"),
        Rasterizer = new StbTrueTypeRasterizer(),
        SuperSampleLevel = superSample,
        HeightPercent = heightPercent,
    });

    [Theory]
    [InlineData(2, 150)]
    [InlineData(4, 50)]
    public void Info_RecordsTheSuperSampleLevelAndHeightPercent(int superSample, int heightPercent)
    {
        var result = Generate(superSample, heightPercent);

        result.Model.Info.Aa.ShouldBe(superSample);
        result.Model.Info.StretchH.ShouldBe(heightPercent);
        result.FntText.ShouldContain($" stretchH={heightPercent} ", Case.Sensitive);
        result.FntText.ShouldContain($" aa={superSample} ", Case.Sensitive);
    }

    [Fact]
    public void Info_Defaults_StayOneAndOneHundred()
    {
        var result = Generate(1, 100);

        result.FntText.ShouldContain(" stretchH=100 ", Case.Sensitive);
        result.FntText.ShouldContain(" aa=1 ", Case.Sensitive);
    }
}
