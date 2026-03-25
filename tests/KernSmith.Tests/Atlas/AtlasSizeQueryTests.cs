using KernSmith.Output;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public class AtlasSizeQueryTests
{
    private static readonly byte[] FontData = LoadTestFont();

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---------------------------------------------------------------
    // 1. Basic query returns valid dimensions
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_BasicAscii_ReturnsDimensions()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        info.Width.ShouldBeGreaterThan(0);
        info.Height.ShouldBeGreaterThan(0);
        info.GlyphCount.ShouldBeGreaterThan(0);
        info.PageCount.ShouldBeGreaterThanOrEqualTo(1, "should have at least one page");
        info.EstimatedEfficiency.ShouldBeGreaterThan(0f, "efficiency should be positive");
    }

    // ---------------------------------------------------------------
    // 2. ForceSquare makes Width == Height
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_ForceSquare_MakesWidthEqualHeight()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SizeConstraints = new AtlasSizeConstraints { ForceSquare = true }
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        info.Width.ShouldBe(info.Height, "ForceSquare should make width equal height");
    }

    // ---------------------------------------------------------------
    // 3. ForcePowerOfTwo rounds to POT
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_ForcePowerOfTwo_RoundsToPot()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SizeConstraints = new AtlasSizeConstraints { ForcePowerOfTwo = true }
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        IsPowerOfTwo(info.Width).ShouldBeTrue($"width {info.Width} should be a power of two");
        IsPowerOfTwo(info.Height).ShouldBeTrue($"height {info.Height} should be a power of two");
    }

    // ---------------------------------------------------------------
    // 4. FixedWidth calculates variable height
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_FixedWidth_CalculatesVariableHeight()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SizeConstraints = new AtlasSizeConstraints { FixedWidth = 512 }
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        info.Width.ShouldBe(512);
        info.Height.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------
    // 5. Null constraints preserves defaults
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_NullConstraints_PreservesDefaults()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
            // SizeConstraints intentionally not set (null)
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        info.Width.ShouldBeGreaterThan(0);
        info.Height.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------
    // 6. Query result matches actual generation
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_ResultMatchesActualGeneration()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        };

        // Act
        var queryInfo = BmFont.QueryAtlasSize(FontData, options);
        var result = BmFont.Generate(FontData, options);

        // Assert — glyph count should match exactly
        queryInfo.GlyphCount.ShouldBe(result.Model.Characters.Count,
            "query glyph count should match actual character count");

        // Atlas dimensions may differ slightly due to heuristics, but should be in the same ballpark.
        // The query estimates size without packing; actual packing may fit tighter or need more space.
        queryInfo.Width.ShouldBeGreaterThan(0);
        queryInfo.Height.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------
    // 7. ForceSquare + ForcePowerOfTwo combined
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_ForceSquareAndPowerOfTwo_BothApplied()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SizeConstraints = new AtlasSizeConstraints
            {
                ForceSquare = true,
                ForcePowerOfTwo = true
            }
        };

        // Act
        var info = BmFont.QueryAtlasSize(FontData, options);

        // Assert
        info.Width.ShouldBe(info.Height, "ForceSquare should make width equal height");
        IsPowerOfTwo(info.Width).ShouldBeTrue($"width {info.Width} should be a power of two");
        IsPowerOfTwo(info.Height).ShouldBeTrue($"height {info.Height} should be a power of two");
    }

    // ---------------------------------------------------------------
    // 8. Constraints match actual generation
    // ---------------------------------------------------------------

    [Fact]
    public void QueryAtlasSize_ConstraintsMatchActualGeneration()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SizeConstraints = new AtlasSizeConstraints
            {
                ForcePowerOfTwo = true,
                ForceSquare = true
            }
        };

        // Act
        var queryInfo = BmFont.QueryAtlasSize(FontData, options);
        var result = BmFont.Generate(FontData, options);

        // Assert — query dimensions should match actual page dimensions exactly
        var actualPage = result.Pages[0];
        queryInfo.Width.ShouldBe(actualPage.Width,
            "query width should match actual generation page width");
        queryInfo.Height.ShouldBe(actualPage.Height,
            "query height should match actual generation page height");
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
