using Bmfontier.Rasterizer;
using FluentAssertions;

namespace Bmfontier.Tests.Rasterizer;

public class LayeredRenderingTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static CharacterSet SingleChar => CharacterSet.FromChars("A");

    #region 1. Gradient only

    [Fact]
    public void Generate_WithGradientOnly_ProducesRgbaOutput()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 48,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientStartG = 0,
            GradientStartB = 0,
            GradientEndR = 0,
            GradientEndG = 0,
            GradientEndB = 255
        });

        // Assert -- output should be RGBA (4 bytes per pixel)
        var page = result.Pages[0];
        page.Format.Should().Be(PixelFormat.Rgba32, "gradient output must be RGBA");
        page.PixelData.Length.Should().Be(page.Width * page.Height * 4);
    }

    [Fact]
    public void Generate_WithGradientOnly_TopPixelsDifferFromBottomPixels()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act -- red-to-blue vertical gradient at large size for clear color separation
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 64,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientStartG = 0,
            GradientStartB = 0,
            GradientEndR = 0,
            GradientEndG = 0,
            GradientEndB = 255
        });

        // Assert -- find the glyph 'A' bounds from the model, then compare top vs bottom pixel colors
        var charA = result.Model.Characters.First(c => c.Id == 65);
        var page = result.Pages[charA.Page];
        var pw = page.Width;

        // Sample a pixel near the top of the glyph and one near the bottom
        var topY = charA.Y + 2;
        var bottomY = charA.Y + charA.Height - 3;
        var midX = charA.X + charA.Width / 2;

        var topIdx = (topY * pw + midX) * 4;
        var bottomIdx = (bottomY * pw + midX) * 4;

        // At least the red or blue channel must differ between top and bottom
        var topR = page.PixelData[topIdx];
        var topB = page.PixelData[topIdx + 2];
        var bottomR = page.PixelData[bottomIdx];
        var bottomB = page.PixelData[bottomIdx + 2];

        // The gradient goes from red (top) to blue (bottom), so we expect
        // the top to be more red and the bottom to be more blue.
        var colorDiffers = (topR != bottomR) || (topB != bottomB);
        colorDiffers.Should().BeTrue(
            "gradient should produce different colors at top ({0},{1}) vs bottom ({2},{3})",
            topR, topB, bottomR, bottomB);
    }

    #endregion

    #region 2. Outline only

    [Fact]
    public void Generate_WithOutlineOnly_ProducesWiderAndTallerGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            Outline = 3
        });

        // Assert
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.Width.Should().BeGreaterThan(charWithout.Width,
            "outlined glyph should be wider than non-outlined glyph");
        charWith.Height.Should().BeGreaterThan(charWithout.Height,
            "outlined glyph should be taller than non-outlined glyph");
    }

    #endregion

    #region 3. Gradient + outline combined

    [Fact]
    public void Generate_WithGradientAndOutline_ProducesRgbaAndIsWiderThanGradientOnly()
    {
        // Arrange
        var fontData = LoadTestFont();

        var gradientOnlyResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientStartG = 0,
            GradientStartB = 0,
            GradientEndR = 0,
            GradientEndG = 0,
            GradientEndB = 255
        });

        // Act
        var combinedResult = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            Outline = 3,
            GradientStartR = 255,
            GradientStartG = 0,
            GradientStartB = 0,
            GradientEndR = 0,
            GradientEndG = 0,
            GradientEndB = 255
        });

        // Assert -- RGBA format
        var page = combinedResult.Pages[0];
        page.Format.Should().Be(PixelFormat.Rgba32, "combined gradient+outline must be RGBA");

        // Assert -- wider than gradient-only because outline adds padding
        var gradOnly = gradientOnlyResult.Model.Characters.First(c => c.Id == 65);
        var combined = combinedResult.Model.Characters.First(c => c.Id == 65);
        combined.Width.Should().BeGreaterThan(gradOnly.Width,
            "gradient+outline glyph should be wider than gradient-only glyph");

        // Assert -- contains gradient colors (not plain white)
        // Look at the center of the glyph where the body should be rendered
        var pw = page.Width;
        var centerX = combined.X + combined.Width / 2;
        var centerY = combined.Y + combined.Height / 2;
        var idx = (centerY * pw + centerX) * 4;

        var r = page.PixelData[idx];
        var g = page.PixelData[idx + 1];
        var b = page.PixelData[idx + 2];
        var a = page.PixelData[idx + 3];

        // The pixel should not be pure white (255,255,255) -- it should show gradient color
        var isNotWhite = r != 255 || g != 255 || b != 255;
        (isNotWhite && a > 0).Should().BeTrue(
            "body pixels should show gradient color, not white (got R={0} G={1} B={2} A={3})",
            r, g, b, a);
    }

    #endregion

    #region 4. Order independence

    [Fact]
    public void Generate_WithSameSettings_ProducesIdenticalOutput()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            Outline = 2,
            GradientStartR = 255,
            GradientStartG = 128,
            GradientStartB = 0,
            GradientEndR = 0,
            GradientEndG = 128,
            GradientEndB = 255
        };

        // Act -- generate twice with identical settings
        var result1 = BmFont.Generate(fontData, options);
        var result2 = BmFont.Generate(fontData, options);

        // Assert -- pixel data should be identical
        var page1 = result1.Pages[0].PixelData;
        var page2 = result2.Pages[0].PixelData;

        page1.Length.Should().Be(page2.Length, "atlas sizes should match");
        page1.Should().BeEquivalentTo(page2, "identical settings must produce identical output");
    }

    #endregion

    #region 5. Shadow only

    [Fact]
    public void Generate_WithShadowOnly_ProducesLargerGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();

        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar
        });

        // Act
        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            ShadowOffsetX = 3,
            ShadowOffsetY = 3
        });

        // Assert
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        // Shadow offset expands the glyph dimensions
        charWith.Width.Should().BeGreaterThan(charWithout.Width,
            "shadowed glyph should be wider than unshadowed glyph");
        charWith.Height.Should().BeGreaterThan(charWithout.Height,
            "shadowed glyph should be taller than unshadowed glyph");
    }

    #endregion

    #region 6. All three effects combined

    [Fact]
    public void Generate_WithAllThreeEffects_ProducesRgbaAndIsLargerThanAnySingleEffect()
    {
        // Arrange
        var fontData = LoadTestFont();

        var gradientOnly = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientEndR = 0
        });

        var outlineOnly = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            Outline = 2
        });

        var shadowOnly = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            ShadowOffsetX = 2,
            ShadowOffsetY = 2
        });

        // Act
        var allEffects = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientEndR = 0,
            Outline = 2,
            ShadowOffsetX = 2,
            ShadowOffsetY = 2
        });

        // Assert -- RGBA output
        allEffects.Pages[0].Format.Should().Be(PixelFormat.Rgba32);

        var allChar = allEffects.Model.Characters.First(c => c.Id == 65);
        var gradChar = gradientOnly.Model.Characters.First(c => c.Id == 65);
        var outChar = outlineOnly.Model.Characters.First(c => c.Id == 65);
        var shadowChar = shadowOnly.Model.Characters.First(c => c.Id == 65);

        // Combined glyph should be at least as large as any single effect
        allChar.Width.Should().BeGreaterThanOrEqualTo(gradChar.Width,
            "combined glyph should be at least as wide as gradient-only");
        allChar.Width.Should().BeGreaterThanOrEqualTo(outChar.Width,
            "combined glyph should be at least as wide as outline-only");
        allChar.Width.Should().BeGreaterThanOrEqualTo(shadowChar.Width,
            "combined glyph should be at least as wide as shadow-only");

        allChar.Height.Should().BeGreaterThanOrEqualTo(gradChar.Height,
            "combined glyph should be at least as tall as gradient-only");
        allChar.Height.Should().BeGreaterThanOrEqualTo(outChar.Height,
            "combined glyph should be at least as tall as outline-only");
        allChar.Height.Should().BeGreaterThanOrEqualTo(shadowChar.Height,
            "combined glyph should be at least as tall as shadow-only");
    }

    #endregion

    #region 7. No effects (defaults)

    [Fact]
    public void Generate_WithDefaults_ProducesGrayscaleOutput()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar
        });

        // Assert -- default output should be grayscale (1 byte per pixel)
        var page = result.Pages[0];
        page.Format.Should().Be(PixelFormat.Grayscale8,
            "default rendering without effects should be grayscale");
        page.PixelData.Length.Should().Be(page.Width * page.Height);
    }

    #endregion

    #region 8. Custom post-processor runs after compositor

    [Fact]
    public void Generate_WithCustomPostProcessor_RunsAfterBuiltInEffects()
    {
        // Arrange
        var fontData = LoadTestFont();
        var customProcessor = new TrackingPostProcessor();

        // Act -- use gradient (handled by compositor) + custom processor (runs after)
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            GradientStartR = 255,
            GradientEndR = 0,
            PostProcessors = new IGlyphPostProcessor[] { customProcessor }
        });

        // Assert
        customProcessor.WasCalled.Should().BeTrue(
            "custom post-processor should run after compositor");
        customProcessor.ProcessedCount.Should().BeGreaterThan(0,
            "custom post-processor should have processed at least one glyph");

        // The custom processor should have received RGBA data (from gradient compositor)
        customProcessor.LastSeenFormat.Should().Be(PixelFormat.Rgba32,
            "custom processor should receive RGBA data from compositor output");
    }

    /// <summary>
    /// A simple post-processor that tracks whether it was called.
    /// </summary>
    private sealed class TrackingPostProcessor : IGlyphPostProcessor
    {
        public bool WasCalled { get; private set; }
        public int ProcessedCount { get; private set; }
        public PixelFormat? LastSeenFormat { get; private set; }

        public RasterizedGlyph Process(RasterizedGlyph glyph)
        {
            WasCalled = true;
            ProcessedCount++;
            LastSeenFormat = glyph.Format;
            return glyph; // pass through unchanged
        }
    }

    #endregion

    #region 9. Legacy OutlinePostProcessor backward compatibility

    [Fact]
    public void Generate_WithLegacyOutlinePostProcessor_StillProducesOutline()
    {
        // Arrange
        var fontData = LoadTestFont();

        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar
        });

        // Act -- use the old-style OutlinePostProcessor in PostProcessors list
        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = SingleChar,
            PostProcessors = new IGlyphPostProcessor[] { new OutlinePostProcessor(3) }
        });

        // Assert -- should still produce wider glyphs (outline extracted by BuildEffects)
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.Width.Should().BeGreaterThan(charWithout.Width,
            "legacy OutlinePostProcessor should still produce wider glyphs via compositor");
        charWith.Height.Should().BeGreaterThan(charWithout.Height,
            "legacy OutlinePostProcessor should still produce taller glyphs via compositor");
    }

    #endregion

    #region 10. Builder API with all effects

    [Fact]
    public void Builder_WithGradientOutlineShadow_GeneratesCorrectly()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(32)
            .WithCharacters(SingleChar)
            .WithGradient((255, 0, 0), (0, 0, 255))
            .WithOutline(2)
            .WithShadow(offsetX: 2, offsetY: 2)
            .Build();

        // Assert
        result.Model.Should().NotBeNull();
        result.Model.Characters.Should().HaveCount(1);
        result.Pages.Should().HaveCountGreaterThan(0);

        // Output must be RGBA (effects produce RGBA)
        result.Pages[0].Format.Should().Be(PixelFormat.Rgba32);

        // Glyph should have positive dimensions
        var charA = result.Model.Characters.First(c => c.Id == 65);
        charA.Width.Should().BeGreaterThan(0);
        charA.Height.Should().BeGreaterThan(0);

        // Atlas should contain rendered pixels
        result.Pages[0].PixelData.Any(b => b != 0).Should().BeTrue(
            "builder-generated atlas with all effects should contain rendered pixels");
    }

    #endregion
}
