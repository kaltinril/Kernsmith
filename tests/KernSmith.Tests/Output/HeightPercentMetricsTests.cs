using KernSmith.Output.Model;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// <see cref="FontGeneratorOptions.HeightPercent"/> stretches the line with the glyphs: BMFont
/// writes <c>lineHeight = ceil(height × scaleH / 100)</c> and <c>base = ceil(ascent × scaleH / 100)</c>
/// (Arial 32 at 150%: 48 and 39 where 100% gives 32 and 26).
/// </summary>
public sealed class HeightPercentMetricsTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontModel Generate(int heightPercent) => BmFont.Generate(Font(), new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.FromChars("HIg"),
        Rasterizer = new StbTrueTypeRasterizer(),
        HeightPercent = heightPercent,
    }).Model;

    [Theory]
    [InlineData(150)]
    [InlineData(50)]
    [InlineData(133)]
    public void LineHeightAndBase_ScaleWithTheStretch(int heightPercent)
    {
        var plain = Generate(100).Common;
        var stretched = Generate(heightPercent).Common;

        stretched.LineHeight.ShouldBe((int)Math.Ceiling((double)plain.LineHeight * heightPercent / 100.0));
        stretched.Base.ShouldBe((int)Math.Ceiling((double)plain.Base * heightPercent / 100.0));
    }

    [Fact]
    public void StretchedGlyph_StaysBetweenTheLineTopAndItsBaseline()
    {
        // 'H' sits on the baseline and reaches the cap height: at 150% its top was above the
        // line (yoffset < 0) while base stayed unstretched.
        var model = Generate(150);
        var h = model.Characters.Single(c => c.Id == 'H');

        h.YOffset.ShouldBeGreaterThanOrEqualTo(0);
        (h.YOffset + h.Height).ShouldBeInRange(model.Common.Base - 1, model.Common.Base + 1);
    }
}
