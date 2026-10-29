using KernSmith.Font;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests that explicitly exercise the RobotoFlex-Variable.ttf fixture by name.
/// Unlike <see cref="VariableFontTests"/>, which discovers any variable font in the
/// Fixtures directory, these tests pin behavior to the known Roboto Flex variable font
/// so the fixture has a clear, named consumer.
/// </summary>
[Collection("RasterizerFactory")]
public class RobotoFlexVariableFontTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RobotoFlex-Variable.ttf");

    private static byte[] LoadRobotoFlex() => File.ReadAllBytes(FixturePath);

    [Fact]
    public void RobotoFlexFixture_Exists()
    {
        // Guards against the fixture being removed or renamed, which would silently
        // make the variable-font tests meaningless.
        File.Exists(FixturePath).ShouldBeTrue(
            $"RobotoFlex-Variable.ttf fixture is expected at {FixturePath}");
    }

    [Fact]
    public void ReadFont_RobotoFlex_HasWeightVariationAxis()
    {
        // Arrange
        var fontData = LoadRobotoFlex();
        var fontReader = new TtfFontReader();

        // Act
        var fontInfo = fontReader.ReadFont(fontData);

        // Assert -- Roboto Flex is a variable font and defines a weight axis
        fontInfo.VariationAxes.ShouldNotBeNull("Roboto Flex should expose an fvar table");
        fontInfo.VariationAxes!.Count.ShouldBeGreaterThan(0);
        fontInfo.VariationAxes.ShouldContain(a => a.Tag == "wght",
            "Roboto Flex defines a weight (wght) axis");
    }

    [Fact]
    public void Generate_RobotoFlex_ProducesValidFont()
    {
        // Arrange
        var fontData = LoadRobotoFlex();
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AaBbCc"),
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(6);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_RobotoFlex_WithWeightAxis_ProducesValidFont()
    {
        // Arrange
        var fontData = LoadRobotoFlex();
        var fontReader = new TtfFontReader();
        var wghtAxis = fontReader.ReadFont(fontData).VariationAxes!
            .FirstOrDefault(a => a.Tag == "wght");
        wghtAxis.ShouldNotBeNull();

        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            VariationAxes = new Dictionary<string, float> { { "wght", wghtAxis!.MaxValue } }
        };

        // Act -- applying a real axis value from the font should generate successfully
        var result = BmFont.Generate(fontData, options);

        // Assert
        result.Model.Characters.Count.ShouldBe(1);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void FreeType_AxesAtTheirDefaults_RenderTheDefaultInstance()
    {
        // FT_Fixed is pointer-sized in the bundled FreeType: coordinates marshalled as 32-bit
        // ints are read in pairs, every axis lands on an extreme, and the "default" instance
        // comes out wide and tall. Naming each axis at its default must change nothing.
        var fontData = LoadRobotoFlex();
        var axes = new TtfFontReader().ReadFont(fontData).VariationAxes!;
        var options = new RasterOptions { Size = 48, Dpi = 72, EnableHinting = false };

        using IRasterizer plain = new FreeTypeRasterizer();
        plain.LoadFont(fontData);
        using IRasterizer varied = new FreeTypeRasterizer();
        varied.LoadFont(fontData);
        varied.SetVariationAxes(axes, axes.ToDictionary(a => a.Tag, a => a.DefaultValue));

        var expected = plain.RasterizeGlyph('A', options)!;
        var actual = varied.RasterizeGlyph('A', options)!;

        (actual.Metrics.Advance, actual.Width, actual.Height).ShouldBe(
            (expected.Metrics.Advance, expected.Width, expected.Height));
        actual.BitmapData.ShouldBe(expected.BitmapData);
    }

    [Fact]
    public void FreeType_WeightAxis_ThickensStemsWithoutMovingCapHeight()
    {
        var fontData = LoadRobotoFlex();
        var axes = new TtfFontReader().ReadFont(fontData).VariationAxes!;
        var options = new RasterOptions { Size = 48, Dpi = 72, EnableHinting = false };

        using IRasterizer regular = new FreeTypeRasterizer();
        regular.LoadFont(fontData);
        using IRasterizer black = new FreeTypeRasterizer();
        black.LoadFont(fontData);
        black.SetVariationAxes(axes, new Dictionary<string, float> { ["wght"] = 1000f });

        var normal = regular.RasterizeGlyph('H', options)!;
        var heavy = black.RasterizeGlyph('H', options)!;

        heavy.Height.ShouldBe(normal.Height);
        heavy.BitmapData.Sum(b => (long)b).ShouldBeGreaterThan(normal.BitmapData.Sum(b => (long)b) * 3 / 2);
    }
}
