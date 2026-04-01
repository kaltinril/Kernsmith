using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Integration;

public class CombinedBatchTests
{
    public CombinedBatchTests()
    {
        if (!RasterizerFactory.GetAvailableBackends().Contains(RasterizerBackend.FreeType))
            RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer());
    }

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---------------------------------------------------------------
    // 1. Combined mode shares pages
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_CombinedMode_SharesPages()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var result = BmFont.GenerateBatch(jobs, options);

        // Assert
        result.SharedPages.ShouldNotBeNull("combined mode should produce shared pages");
        result.SharedPages!.Count.ShouldBeGreaterThan(0);
        result.Results.Count.ShouldBe(2);
        result.Results[0].Success.ShouldBeTrue();
        result.Results[1].Success.ShouldBeTrue();

        // Both font results should reference the shared pages (same page file names in model)
        var pages0 = result.Results[0].Result!.Model.Pages;
        var pages1 = result.Results[1].Result!.Model.Pages;
        pages0.Select(p => p.File).ShouldBe(pages1.Select(p => p.File),
            "both fonts should reference the same shared page file names");
    }

    // ---------------------------------------------------------------
    // 2. Separate mode preserves behavior
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_SeparateMode_PreservesBehavior()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Separate };

        // Act
        var result = BmFont.GenerateBatch(jobs, options);

        // Assert
        result.SharedPages.ShouldBeNull("separate mode should not produce shared pages");
        result.Results.Count.ShouldBe(2);
        result.Results[0].Success.ShouldBeTrue();
        result.Results[1].Success.ShouldBeTrue();

        // Each result should have its own pages
        result.Results[0].Result!.Pages.Count.ShouldBeGreaterThan(0);
        result.Results[1].Result!.Pages.Count.ShouldBeGreaterThan(0);
    }

    // ---------------------------------------------------------------
    // 3. Different texture formats throws
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_DifferentTextureFormats_Throws()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, TextureFormat = TextureFormat.Png }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, TextureFormat = TextureFormat.Tga }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var act = () => BmFont.GenerateBatch(jobs, options);

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    // ---------------------------------------------------------------
    // 4. Same font, different sizes — game-engine use case
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_SameFontDifferentSizes_CombinedMode_ValidCharacters()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var result = BmFont.GenerateBatch(jobs, options);

        // Assert — both results should have valid characters with positions within atlas bounds
        var sharedPage = result.SharedPages![0];
        foreach (var jobResult in result.Results)
        {
            jobResult.Success.ShouldBeTrue();
            var model = jobResult.Result!.Model;
            model.Characters.Count.ShouldBeGreaterThan(0);

            foreach (var ch in model.Characters)
            {
                ch.X.ShouldBeGreaterThanOrEqualTo(0, $"char {ch.Id} X should be >= 0");
                ch.Y.ShouldBeGreaterThanOrEqualTo(0, $"char {ch.Id} Y should be >= 0");
                (ch.X + ch.Width).ShouldBeLessThanOrEqualTo(sharedPage.Width,
                    $"char {ch.Id} should fit within atlas width");
                (ch.Y + ch.Height).ShouldBeLessThanOrEqualTo(sharedPage.Height,
                    $"char {ch.Id} should fit within atlas height");
            }
        }
    }

    // ---------------------------------------------------------------
    // 5. Channel packing with combined mode throws
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_ChannelPackingWithCombined_Throws()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, ChannelPacking = true }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32 }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var act = () => BmFont.GenerateBatch(jobs, options);

        // Assert
        Should.Throw<InvalidOperationException>(act);
    }

    // ---------------------------------------------------------------
    // 6. Combined mode preserves glyph count vs separate mode
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_CombinedMode_PreservesGlyphCount()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };

        // Act — generate in both modes
        var combinedResult = BmFont.GenerateBatch(jobs,
            new BatchOptions { AtlasMode = BatchAtlasMode.Combined });
        var separateResult = BmFont.GenerateBatch(jobs,
            new BatchOptions { AtlasMode = BatchAtlasMode.Separate });

        // Assert — each font should have the same character count in both modes
        for (var i = 0; i < jobs.Length; i++)
        {
            var combinedCount = combinedResult.Results[i].Result!.Model.Characters.Count;
            var separateCount = separateResult.Results[i].Result!.Model.Characters.Count;
            combinedCount.ShouldBe(separateCount,
                $"job {i} should have the same glyph count in combined and separate modes");
        }
    }

    // ---------------------------------------------------------------
    // 7. Combined mode shared pages contain pixel data
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_CombinedMode_SharedPagesContainPixelData()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var result = BmFont.GenerateBatch(jobs, options);

        // Assert — shared pages should have non-zero pixel data (glyphs were actually rendered)
        result.SharedPages.ShouldNotBeNull();
        result.SharedPages![0].PixelData.ShouldContain(b => b != 0,
            "shared page pixel data should contain rendered glyph pixels, not all zeros");
    }

    // ---------------------------------------------------------------
    // 8. No character overlap between fonts in combined mode
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateBatch_CombinedMode_NoCharacterOverlap()
    {
        // Arrange
        var fontData = LoadTestFont();
        var jobs = new[]
        {
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 16, Characters = CharacterSet.Ascii }
            },
            new BatchJob
            {
                FontData = fontData,
                Options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.Ascii }
            }
        };
        var options = new BatchOptions { AtlasMode = BatchAtlasMode.Combined };

        // Act
        var result = BmFont.GenerateBatch(jobs, options);

        // Assert — characters from different fonts should not occupy the exact same rectangle
        var chars0 = result.Results[0].Result!.Model.Characters;
        var chars1 = result.Results[1].Result!.Model.Characters;

        foreach (var c0 in chars0)
        {
            foreach (var c1 in chars1)
            {
                // Skip zero-size characters (e.g., space)
                if (c0.Width == 0 && c0.Height == 0) continue;
                if (c1.Width == 0 && c1.Height == 0) continue;

                var sameRect = c0.X == c1.X && c0.Y == c1.Y
                    && c0.Width == c1.Width && c0.Height == c1.Height;
                sameRect.ShouldBeFalse(
                    $"char {c0.Id} from font 0 and char {c1.Id} from font 1 should not share the exact same rectangle ({c0.X},{c0.Y} {c0.Width}x{c0.Height})");
            }
        }
    }
}
