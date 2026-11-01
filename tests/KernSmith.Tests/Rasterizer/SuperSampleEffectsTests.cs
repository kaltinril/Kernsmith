using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Effect sizes are output pixels at every super-sample level: an outline or a shadow offset
/// grows a glyph's cell by the same amount at <see cref="FontGeneratorOptions.SuperSampleLevel"/>
/// 2 or 4 as at 1 (BMFont adds its outline after the downscale, too).
/// </summary>
public sealed class SuperSampleEffectsTests
{
    private const string Chars = "Il";

    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Outline_GrowsEachCellByTwiceItsWidth(int level)
    {
        var plain = Cells(level, _ => { });
        var outlined = Cells(level, o => o.Outline = 2);

        foreach (var c in Chars)
        {
            outlined[c].Width.ShouldBe(plain[c].Width + 4, $"'{c}' at {level}x");
            outlined[c].Height.ShouldBe(plain[c].Height + 4, $"'{c}' at {level}x");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ShadowOffset_GrowsEachCellAsAtTheFinalSize(int level)
    {
        static void Shadow(FontGeneratorOptions o)
        {
            o.ShadowOffsetX = 4;
            o.ShadowOffsetY = 6;
            o.ShadowBlur = 0;
        }

        var growthAt1 = Growth(1, Shadow);
        var growthAtLevel = Growth(level, Shadow);

        foreach (var c in Chars)
            growthAtLevel[c].ShouldBe(growthAt1[c], $"'{c}' at {level}x");
    }

    private static Dictionary<char, (int W, int H)> Growth(int level, Action<FontGeneratorOptions> effect)
    {
        var plain = Cells(level, _ => { });
        var styled = Cells(level, effect);
        return Chars.ToDictionary(c => c, c => (styled[c].Width - plain[c].Width, styled[c].Height - plain[c].Height));
    }

    private static Dictionary<char, (int Width, int Height)> Cells(int level, Action<FontGeneratorOptions> configure)
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(Chars),
            Rasterizer = new StbTrueTypeRasterizer(),
            SuperSampleLevel = level,
        };
        configure(options);

        var model = BmFont.Generate(Font(), options).Model;
        return Chars.ToDictionary(c => c, c =>
        {
            var entry = model.Characters.Single(e => e.Id == c);
            return (entry.Width, entry.Height);
        });
    }
}
