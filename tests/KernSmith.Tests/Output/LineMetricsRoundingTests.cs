using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// Issue #266: with BMFont cell sizing the requested size becomes a fractional ppem
/// (<c>size × upem / (winAscent + winDescent)</c>), stored as a <see cref="float"/>, so the line
/// height scaled back from it lands a hair off the exact integer <c>size</c>. A plain ceiling turned
/// a hair above into <c>size + 1</c>. These tests pin that a line box equal to a whole pixel count
/// within float noise stays at that count, while a genuinely fractional one still rounds up.
/// </summary>
[Collection("RasterizerFactory")]
public class LineMetricsRoundingTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> CellSizedFonts =>
    [
        "Roboto-Regular.ttf",
        "SourceSerif4-Regular.otf",
        "DancingScript-Variable.ttf",
        "SourceCodeVF-Upright.otf",
        "NotoSans-Regular-Latin.ttf",
        "RobotoFlex-Latin.ttf",
    ];

    private static byte[] Load(string file) => File.ReadAllBytes(Path.Combine(FixturesDir, file));

    /// <summary>The fractional ppem the pipeline rasterizes at for a BMFont cell height.</summary>
    private static float CellSizedPpem(FontInfo font, int size) =>
        (float)((double)size * font.UnitsPerEm / (font.Os2!.WinAscent + font.Os2.WinDescent));

    [Theory]
    [MemberData(nameof(CellSizedFonts))]
    public void ComputeLineMetrics_CellSizedPpem_LineHeightIsExactlyTheRequestedSize(string file)
    {
        var font = BmFont.ReadFontInfo(Load(file));

        var wrong = new List<string>();
        for (int size = 4; size <= 256; size++)
        {
            var (lineHeight, _) = BmFontModelBuilder.ComputeLineMetrics(font, CellSizedPpem(font, size), null);
            if (lineHeight != size)
                wrong.Add($"{size}->{lineHeight}");
        }

        wrong.ShouldBeEmpty($"{file}: lineHeight should equal the requested cell size");
    }

    [Theory]
    [InlineData("Roboto-Regular.ttf", 32)]
    [InlineData("DancingScript-Variable.ttf", 48)]
    public void Generate_FreeType_LineHeightIsTheRequestedSize(string file, int size)
    {
        // The issue's FreeType examples, end to end through the .fnt.
        var result = BmFont.Generate(Load(file), new FontGeneratorOptions
        {
            Size = size,
            Characters = CharacterSet.FromChars("AgÉ"),
            Backend = RasterizerBackend.FreeType
        });

        BmFontReader.ReadText(result.FntText).Common.LineHeight.ShouldBe(size);
    }

    [Fact]
    public void ComputeLineMetrics_GenuinelyFractionalLineBox_StillRoundsUp()
    {
        // At ppem 32 (MatchCharHeight), Roboto's win box is 2701 × 32 / 2048 = 42.203 px and its
        // ascent 2146 × 32 / 2048 = 33.53 px: both must round up, or the box clips glyphs.
        var font = BmFont.ReadFontInfo(Load("Roboto-Regular.ttf"));

        var (lineHeight, baseLine) = BmFontModelBuilder.ComputeLineMetrics(font, 32f, null);

        lineHeight.ShouldBe(43);
        baseLine.ShouldBe(34);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(32.0, 32)]
    [InlineData(32.000002, 32)]     // float noise above a whole pixel
    [InlineData(31.999998, 32)]     // float noise below a whole pixel
    [InlineData(1000.00005, 1000)]  // noise scales with magnitude
    [InlineData(32.001, 33)]        // a real fraction, however small, rounds up
    [InlineData(32.2, 33)]
    [InlineData(32.9, 33)]
    [InlineData(0.4, 1)]
    public void CeilingToPixels_IgnoresFloatNoiseButRoundsRealFractionsUp(double pixels, int expected)
    {
        RasterizerFontMetrics.CeilingToPixels(pixels).ShouldBe(expected);
    }

    [Fact]
    public void StbTrueType_CellSizedPpem_LineHeightIsExactlyTheRequestedSize()
    {
        // StbTrueType measures hhea (ascender - descender + lineGap), which equals the win box in
        // Source Serif 4 (the only such fixture Stb can load; it rejects CFF2), so its cell-sized
        // line height is mathematically the size too.
        const string file = "SourceSerif4-Regular.otf";
        var bytes = Load(file);
        var font = BmFont.ReadFontInfo(bytes);
        using IRasterizer stb = new StbTrueTypeRasterizer();
        stb.LoadFont(bytes);

        var wrong = new List<string>();
        for (int size = 4; size <= 256; size++)
        {
            var metrics = stb.GetFontMetrics(new RasterOptions { Size = CellSizedPpem(font, size) });
            if (metrics!.LineHeight != size)
                wrong.Add($"{size}->{metrics.LineHeight}");
        }

        wrong.ShouldBeEmpty($"{file}: StbTrueType lineHeight should equal the requested cell size");
    }
}
