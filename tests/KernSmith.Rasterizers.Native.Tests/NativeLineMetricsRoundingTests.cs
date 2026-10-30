using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Issue #266: with BMFont cell sizing the requested size reaches the backend as a fractional
/// <see cref="float"/> ppem (<c>size × upem / (winAscent + winDescent)</c>), so the win box scaled
/// back from it lands a hair off the exact integer <c>size</c>, and a plain ceiling made the line
/// height <c>size + 1</c> for roughly half of all sizes. Native must report the requested size, the
/// same value the shared calculation gives FreeType.
/// </summary>
public class NativeLineMetricsRoundingTests
{
    private static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", file);

    public static TheoryData<string> CellSizedFonts =>
    [
        "Roboto-Regular.ttf",
        "SourceSerif4-Regular.otf",
        "DancingScript-Variable.ttf",
        "SourceCodeVF-Upright.otf",
        "NotoSans-Regular-Latin.ttf",
        "RobotoFlex-Latin.ttf",
    ];

    [Theory]
    [MemberData(nameof(CellSizedFonts))]
    public void GetFontMetrics_CellSizedPpem_LineHeightIsExactlyTheRequestedSize(string file)
    {
        var bytes = File.ReadAllBytes(Fixture(file));
        var font = BmFont.ReadFontInfo(bytes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var wrong = new List<string>();
        for (int size = 4; size <= 256; size++)
        {
            // The ppem PreparedRasterization hands the backend for a BMFont cell height.
            float ppem = (float)((double)size * font.UnitsPerEm / (font.Os2!.WinAscent + font.Os2.WinDescent));
            var metrics = native.GetFontMetrics(new RasterOptions { Size = ppem });
            if (metrics!.LineHeight != size)
                wrong.Add($"{size}->{metrics.LineHeight}");
        }

        wrong.ShouldBeEmpty($"{file}: lineHeight should equal the requested cell size");
    }

    [Theory]
    [InlineData("Roboto-Regular.ttf", 32)]
    [InlineData("SourceSerif4-Regular.otf", 24)]
    [InlineData("DancingScript-Variable.ttf", 48)]
    [InlineData("SourceCodeVF-Upright.otf", 72)]
    public void Generate_NativeAndFreeType_ReportTheRequestedLineHeightAndTheSameBase(string file, int size)
    {
        // The issue's table, end to end: each backend wrote size + 1 for some of these, and the
        // two disagreed with each other.
        var bytes = File.ReadAllBytes(Fixture(file));

        CommonBlock Common(RasterizerBackend backend) => BmFontReader.ReadText(BmFont.Generate(bytes, new FontGeneratorOptions
        {
            Size = size,
            Characters = CharacterSet.FromChars("AgÉ"),
            Backend = backend
        }).FntText).Common;

        var native = Common(RasterizerBackend.Native);
        var freeType = Common(RasterizerBackend.FreeType);

        native.LineHeight.ShouldBe(size, "Native lineHeight");
        freeType.LineHeight.ShouldBe(size, "FreeType lineHeight");
        native.Base.ShouldBe(freeType.Base, "Native and FreeType base");
    }

    [Fact]
    public void GetFontMetrics_GenuinelyFractionalLineBox_StillRoundsUp()
    {
        // At ppem 32 Roboto's win box is 2701 × 32 / 2048 = 42.203 px, its ascent 33.53 px and
        // its descent 8.67 px: all must round up, or the box clips glyphs.
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());

        var metrics = native.GetFontMetrics(new RasterOptions { Size = 32 })!;

        metrics.LineHeight.ShouldBe(43);
        metrics.Ascent.ShouldBe(34);
        metrics.Descent.ShouldBe(9);
    }
}
