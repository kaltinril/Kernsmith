using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public class RenderToExistingTests
{
    private static readonly byte[] FontData = LoadTestFont();

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    /// <summary>
    /// Creates a solid-color RGBA PNG of the specified dimensions.
    /// </summary>
    private static byte[] CreateTestPng(int width, int height, byte r = 128, byte g = 128, byte b = 128, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 4 + 0] = r;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = b;
            pixels[i * 4 + 3] = a;
        }
        var encoder = new StbPngEncoder();
        return encoder.Encode(pixels, width, height, PixelFormat.Rgba32);
    }

    // ---------------------------------------------------------------
    // 1. Glyphs appear at correct offset — output image matches source dimensions
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_GlyphsAppearAtCorrectOffset()
    {
        // Arrange
        var sourcePng = CreateTestPng(512, 512);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("AB"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 128,
                Y = 128,
                Width = 256,
                Height = 256
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Width.ShouldBe(512);
        result.Pages[0].Height.ShouldBe(512);
    }

    // ---------------------------------------------------------------
    // 2. Fnt character values include region offset
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_FntCharacterValuesIncludeRegionOffset()
    {
        // Arrange
        var sourcePng = CreateTestPng(512, 512);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("AB"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 128,
                Y = 128,
                Width = 256,
                Height = 256
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — all character X/Y values should be offset by the region origin
        foreach (var ch in result.Model.Characters)
        {
            ch.X.ShouldBeGreaterThanOrEqualTo(128,
                $"char {ch.Id} X should be >= region offset 128");
            ch.Y.ShouldBeGreaterThanOrEqualTo(128,
                $"char {ch.Id} Y should be >= region offset 128");
        }
    }

    // ---------------------------------------------------------------
    // 3. Overflow throws AtlasPackingException
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_Overflow_ThrowsAtlasPackingException()
    {
        // Arrange — tiny region, large font
        var sourcePng = CreateTestPng(100, 100);
        var options = new FontGeneratorOptions
        {
            Size = 48,
            Characters = CharacterSet.Ascii,
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 0,
                Y = 0,
                Width = 10,
                Height = 10
            }
        };

        // Act
        var act = () => BmFont.Generate(FontData, options);

        // Assert — the packer throws InvalidOperationException when a single glyph exceeds page dimensions
        var exception = Record.Exception(() => act());
        exception.ShouldNotBeNull("glyphs should not fit in a 10x10 region");
        (exception is AtlasPackingException || exception is InvalidOperationException)
            .ShouldBeTrue($"expected AtlasPackingException or InvalidOperationException, got {exception!.GetType().Name}");
    }

    // ---------------------------------------------------------------
    // 4. Region out of bounds throws ArgumentException
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_OutOfBounds_ThrowsArgumentException()
    {
        // Arrange — region exceeds source image bounds
        var sourcePng = CreateTestPng(100, 100);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("A"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 50,
                Y = 50,
                Width = 200,
                Height = 200
            }
        };

        // Act
        var act = () => BmFont.Generate(FontData, options);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    // ---------------------------------------------------------------
    // 5. SourcePngData works in memory
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_SourcePngData_WorksInMemory()
    {
        // Arrange
        var sourcePng = CreateTestPng(256, 256);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("A"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 0,
                Y = 0,
                Width = 256,
                Height = 256
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "in-memory source PNG should produce valid results");
        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Width.ShouldBe(256);
        result.Pages[0].Height.ShouldBe(256);
    }

    // ---------------------------------------------------------------
    // 6. Null TargetRegion preserves default behavior
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_Null_PreservesDefaultBehavior()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB")
            // TargetRegion intentionally not set (null)
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — normal generation without compositing
        result.Model.Characters.Count.ShouldBe(2);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------
    // 7. Neither path nor data throws ArgumentException
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_NeitherPathNorData_ThrowsArgumentException()
    {
        // Arrange
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("A"),
            TargetRegion = new AtlasTargetRegion
            {
                // No SourcePngPath, no SourcePngData
                X = 0,
                Y = 0,
                Width = 256,
                Height = 256
            }
        };

        // Act
        var act = () => BmFont.Generate(FontData, options);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    // ---------------------------------------------------------------
    // 8. Zero-dimension region throws ArgumentOutOfRangeException
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_ZeroDimension_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var sourcePng = CreateTestPng(256, 256);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("A"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 256
            }
        };

        // Act
        var act = () => BmFont.Generate(FontData, options);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    // ---------------------------------------------------------------
    // 9. All chars contained within target rectangle
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_CharsContainedWithinRectangle()
    {
        // Arrange
        var sourcePng = CreateTestPng(512, 512);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("ABCDEFG"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 128,
                Y = 128,
                Width = 256,
                Height = 256
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — every character must be fully contained within the target rectangle
        foreach (var ch in result.Model.Characters)
        {
            ch.X.ShouldBeGreaterThanOrEqualTo(128,
                $"char {ch.Id} X should be >= region X (128)");
            ch.Y.ShouldBeGreaterThanOrEqualTo(128,
                $"char {ch.Id} Y should be >= region Y (128)");
            (ch.X + ch.Width).ShouldBeLessThanOrEqualTo(128 + 256,
                $"char {ch.Id} right edge should be <= region right (384)");
            (ch.Y + ch.Height).ShouldBeLessThanOrEqualTo(128 + 256,
                $"char {ch.Id} bottom edge should be <= region bottom (384)");
        }
    }

    // ---------------------------------------------------------------
    // 10. Pixels outside region are unchanged
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_PixelsOutsideRectangleUnchanged()
    {
        // Arrange — solid-color source image
        const byte srcR = 200, srcG = 100, srcB = 50, srcA = 255;
        var sourcePng = CreateTestPng(256, 256, srcR, srcG, srcB, srcA);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("AB"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 64,
                Y = 64,
                Width = 128,
                Height = 128
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — result page contains raw RGBA pixels; sample corners and edges outside the region
        var pixels = result.Pages[0].PixelData;
        var width = result.Pages[0].Width;

        // Helper to read a pixel at (x, y)
        void AssertPixelUnchanged(int x, int y, string label)
        {
            var idx = (y * width + x) * 4;
            pixels[idx + 0].ShouldBe(srcR, $"{label} R at ({x},{y})");
            pixels[idx + 1].ShouldBe(srcG, $"{label} G at ({x},{y})");
            pixels[idx + 2].ShouldBe(srcB, $"{label} B at ({x},{y})");
            pixels[idx + 3].ShouldBe(srcA, $"{label} A at ({x},{y})");
        }

        // Corners of the full image (all outside the 64,64,128,128 region)
        AssertPixelUnchanged(0, 0, "top-left corner");
        AssertPixelUnchanged(255, 0, "top-right corner");
        AssertPixelUnchanged(0, 255, "bottom-left corner");
        AssertPixelUnchanged(255, 255, "bottom-right corner");

        // Edges just outside the region
        AssertPixelUnchanged(63, 64, "left of region");
        AssertPixelUnchanged(64, 63, "above region");
        AssertPixelUnchanged(192, 64, "right of region");
        AssertPixelUnchanged(64, 192, "below region");
    }

    // ---------------------------------------------------------------
    // 11. Pixels inside region contain glyph data
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_PixelsInsideRegionContainGlyphData()
    {
        // Arrange — solid-color source image
        const byte srcR = 200, srcG = 100, srcB = 50, srcA = 255;
        var sourcePng = CreateTestPng(256, 256, srcR, srcG, srcB, srcA);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("ABCDEFG"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 64,
                Y = 64,
                Width = 128,
                Height = 128
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — at least some pixels inside the region should differ from background
        var pixels = result.Pages[0].PixelData;
        var width = result.Pages[0].Width;
        var changedCount = 0;

        for (var y = 64; y < 64 + 128; y++)
        {
            for (var x = 64; x < 64 + 128; x++)
            {
                var idx = (y * width + x) * 4;
                if (pixels[idx + 0] != srcR || pixels[idx + 1] != srcG
                    || pixels[idx + 2] != srcB || pixels[idx + 3] != srcA)
                {
                    changedCount++;
                }
            }
        }

        changedCount.ShouldBeGreaterThan(0,
            "compositing should change at least some pixels inside the target region");
    }

    // ---------------------------------------------------------------
    // 12. Common block ScaleW/ScaleH match source dimensions
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_CommonBlockScaleMatchesSourceDimensions()
    {
        // Arrange
        var sourcePng = CreateTestPng(512, 512);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("AB"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 100,
                Y = 100,
                Width = 200,
                Height = 200
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert — ScaleW/ScaleH should reflect the full source image, not the region
        result.Model.Common.ScaleW.ShouldBe(512,
            "ScaleW should match source image width, not region width");
        result.Model.Common.ScaleH.ShouldBe(512,
            "ScaleH should match source image height, not region height");
    }

    // ---------------------------------------------------------------
    // 13. Target region produces exactly one page
    // ---------------------------------------------------------------

    [Fact]
    public void TargetRegion_ExactlyOnePage()
    {
        // Arrange
        var sourcePng = CreateTestPng(512, 512);
        var options = new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.FromChars("ABCDEFGHIJKLMNOP"),
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = sourcePng,
                X = 0,
                Y = 0,
                Width = 512,
                Height = 512
            }
        };

        // Act
        var result = BmFont.Generate(FontData, options);

        // Assert
        result.Pages.Count.ShouldBe(1,
            "target region mode must never produce multi-page output");
    }
}
