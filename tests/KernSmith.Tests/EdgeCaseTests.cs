using KernSmith.Rasterizer;
using FluentAssertions;

namespace KernSmith.Tests;

/// <summary>
/// Track B4 — Feature edge case tests from Phase 12 pre-ship polish plan.
/// </summary>
public class EdgeCaseTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static CharacterSet AsciiLetters => CharacterSet.FromChars("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    #region B4.1 — CustomGlyph replacement

    [Fact]
    public void Generate_WithCustomGlyph_ReplacesExistingCharacter()
    {
        // Arrange
        var fontData = LoadTestFont();
        var customWidth = 20;
        var customHeight = 25;
        var customPixels = new byte[customWidth * customHeight * 4];
        // Fill with a recognizable pattern (solid red, fully opaque)
        for (var i = 0; i < customPixels.Length; i += 4)
        {
            customPixels[i] = 255;     // R
            customPixels[i + 1] = 0;   // G
            customPixels[i + 2] = 0;   // B
            customPixels[i + 3] = 255; // A
        }

        // Act — replace 'A' (codepoint 65) with a custom glyph
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB"),
            CustomGlyphs = new Dictionary<int, CustomGlyph>
            {
                [65] = new CustomGlyph(customWidth, customHeight, customPixels)
            }
        });

        // Assert — character 'A' should have the custom glyph dimensions
        var charA = result.Model.Characters.First(c => c.Id == 65);
        charA.Width.Should().Be(customWidth,
            "custom glyph should replace the rasterized glyph with the specified width");
        charA.Height.Should().Be(customHeight,
            "custom glyph should replace the rasterized glyph with the specified height");

        // 'B' should still be rasterized normally (not the custom dimensions)
        var charB = result.Model.Characters.First(c => c.Id == 66);
        charB.Width.Should().NotBe(customWidth,
            "'B' should retain its normal rasterized dimensions");
    }

    [Fact]
    public void Generate_WithCustomGlyph_AddsNewCodepoint()
    {
        // Arrange — add a custom glyph for a codepoint not in the character set
        var fontData = LoadTestFont();
        var customWidth = 16;
        var customHeight = 16;
        var customPixels = new byte[customWidth * customHeight * 4];
        for (var i = 0; i < customPixels.Length; i += 4)
        {
            customPixels[i + 3] = 255; // opaque
        }

        // Codepoint 0xE000 is in the Private Use Area — unlikely to exist in Roboto
        const int privateUseCodepoint = 0xE000;

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            CustomGlyphs = new Dictionary<int, CustomGlyph>
            {
                [privateUseCodepoint] = new CustomGlyph(customWidth, customHeight, customPixels)
            }
        });

        // Assert — the custom codepoint should appear in the output
        var customChar = result.Model.Characters.FirstOrDefault(c => c.Id == privateUseCodepoint);
        customChar.Should().NotBeNull(
            "custom glyph for a new codepoint should be added to the output");
        customChar!.Width.Should().Be(customWidth);
        customChar.Height.Should().Be(customHeight);
    }

    [Fact]
    public void Generate_WithCustomGlyph_UsesCustomXAdvance()
    {
        // Arrange
        var fontData = LoadTestFont();
        var customWidth = 20;
        var customHeight = 25;
        var customAdvance = 30;
        var customPixels = new byte[customWidth * customHeight * 4];
        for (var i = 0; i < customPixels.Length; i += 4)
            customPixels[i + 3] = 255;

        // Act — replace 'A' with a custom glyph that has a specific XAdvance
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            CustomGlyphs = new Dictionary<int, CustomGlyph>
            {
                [65] = new CustomGlyph(customWidth, customHeight, customPixels, XAdvance: customAdvance)
            }
        });

        // Assert — XAdvance in the output should match the custom value
        var charA = result.Model.Characters.First(c => c.Id == 65);
        charA.XAdvance.Should().Be(customAdvance,
            "custom glyph XAdvance should be reflected in the output");
    }

    #endregion

    #region B4.2 — MatchCharHeight two-pass

    [Fact]
    public void Generate_WithMatchCharHeight_ProducesConsistentHeight()
    {
        // Arrange
        var fontData = LoadTestFont();
        var requestedSize = 32;

        // Act — generate with MatchCharHeight enabled
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = requestedSize,
            Characters = AsciiLetters,
            MatchCharHeight = true
        });

        // Assert — the tallest rendered glyph should be close to the requested pixel size.
        // MatchCharHeight does a two-pass render: first pass measures, second pass rescales.
        var maxGlyphHeight = result.Model.Characters.Max(c => c.Height);
        maxGlyphHeight.Should().BeGreaterThan(0,
            "at least one glyph should have positive height");

        // The tallest glyph should be within a reasonable range of the requested size.
        // Allow some tolerance for rounding and padding.
        maxGlyphHeight.Should().BeCloseTo(requestedSize, 4,
            "MatchCharHeight should rescale so the tallest glyph is close to the requested pixel size");
    }

    [Fact]
    public void Generate_WithMatchCharHeight_DiffersFromWithout()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            MatchCharHeight = false
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            MatchCharHeight = true
        });

        // Assert — the two-pass adjustment should produce different glyph heights
        // because the em-size rendering typically doesn't match the pixel height exactly.
        var maxWithout = resultWithout.Model.Characters.Max(c => c.Height);
        var maxWith = resultWith.Model.Characters.Max(c => c.Height);

        // They should differ (the two-pass rescale adjusts the effective size)
        maxWith.Should().NotBe(maxWithout,
            "MatchCharHeight two-pass should produce different glyph heights than default rendering");
    }

    #endregion

    #region B4.3 — EqualizeCellHeights

    [Fact]
    public void Generate_WithEqualizeCellHeights_AllGlyphsSameHeight()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            EqualizeCellHeights = true
        });

        // Assert — all characters should have the same height after equalization
        var heights = result.Model.Characters.Select(c => c.Height).Distinct().ToList();
        heights.Should().HaveCount(1,
            "EqualizeCellHeights should pad all glyphs to the same height");
    }

    [Fact]
    public void Generate_WithEqualizeCellHeights_HeightIsMaxOfAllGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act — generate without equalization to find the natural max height
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            EqualizeCellHeights = false
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            EqualizeCellHeights = true
        });

        // Assert — the equalized height should equal the max natural height
        var maxNaturalHeight = resultWithout.Model.Characters.Max(c => c.Height);
        var equalizedHeight = resultWith.Model.Characters.First().Height;

        equalizedHeight.Should().Be(maxNaturalHeight,
            "equalized height should match the tallest glyph from non-equalized rendering");
    }

    [Fact]
    public void Generate_WithoutEqualizeCellHeights_GlyphsHaveVaryingHeights()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = AsciiLetters,
            EqualizeCellHeights = false
        });

        // Assert — without equalization, different characters should have different heights
        // (e.g., 'A' is taller than 'a', lowercase letters vary in height)
        var heights = result.Model.Characters.Select(c => c.Height).Distinct().ToList();
        heights.Count.Should().BeGreaterThan(1,
            "without equalization, glyphs should have varying heights");
    }

    #endregion

    #region B4.4 — Extended metadata reflection

    [Fact]
    public void Generate_WithOutlinePostProcessor_SetsOutlineThicknessInMetadata()
    {
        // Arrange
        var fontData = LoadTestFont();
        var outlineWidth = 3;

        // Act — use OutlinePostProcessor in PostProcessors list so metadata reflection picks it up
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            PostProcessors = new IGlyphPostProcessor[] { new OutlinePostProcessor(outlineWidth) }
        });

        // Assert — extended metadata should contain the outline thickness
        result.Model.Extended.Should().NotBeNull(
            "generation with outline should produce extended metadata");
        result.Model.Extended!.OutlineThickness.Should().Be(outlineWidth,
            "OutlineThickness in metadata should match the configured outline width");
    }

    [Fact]
    public void Generate_WithGradientPostProcessor_SetsGradientColorsInMetadata()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act — use gradient via PostProcessors for metadata reflection
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            PostProcessors = new IGlyphPostProcessor[]
            {
                new GradientPostProcessor(255, 215, 0, 220, 20, 60)
            }
        });

        // Assert — extended metadata should contain gradient colors
        result.Model.Extended.Should().NotBeNull(
            "generation with gradient should produce extended metadata");
        result.Model.Extended!.GradientTopColor.Should().Be("FFD700",
            "gradient top color should be serialized as hex RGB");
        result.Model.Extended!.GradientBottomColor.Should().Be("DC143C",
            "gradient bottom color should be serialized as hex RGB");
    }

    [Fact]
    public void Generate_WithNoEffects_ExtendedMetadataHasNoExtendedFields()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A")
        });

        // Assert — without effects, extended metadata should either be null
        // or have no extended fields (only GeneratorVersion)
        if (result.Model.Extended != null)
        {
            result.Model.Extended.OutlineThickness.Should().BeNull(
                "no outline was configured");
            result.Model.Extended.GradientTopColor.Should().BeNull(
                "no gradient was configured");
            result.Model.Extended.SdfSpread.Should().BeNull(
                "SDF was not enabled");
        }
    }

    #endregion

    #region B4.5 — Super-sampling metric accuracy

    [Fact]
    public void Generate_WithSuperSampling_MetricsAreScaledDown()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");

        // Act — generate at 1x and 2x super sampling
        var result1x = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            SuperSampleLevel = 1
        });

        var result2x = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            SuperSampleLevel = 2
        });

        // Assert — with 2x super sampling, the glyph is rasterized at 2x then downscaled.
        // The final output metrics should be similar to 1x (same requested size),
        // not 2x the size.
        var char1x = result1x.Model.Characters.First(c => c.Id == 65);
        var char2x = result2x.Model.Characters.First(c => c.Id == 65);

        // Width and height should be similar (within a small tolerance for rounding)
        char2x.Width.Should().BeCloseTo(char1x.Width, 2,
            "super-sampled glyph width should be close to non-super-sampled width after downscale");
        char2x.Height.Should().BeCloseTo(char1x.Height, 2,
            "super-sampled glyph height should be close to non-super-sampled height after downscale");

        // XAdvance should also be similar
        char2x.XAdvance.Should().BeCloseTo(char1x.XAdvance, 2,
            "super-sampled XAdvance should be close to non-super-sampled XAdvance after downscale");
    }

    [Fact]
    public void Generate_WithSuperSampling_ProducesValidAtlas()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            SuperSampleLevel = 2
        });

        // Assert — the result should be valid and contain rendered glyphs
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "super-sampled generation should produce characters");
        result.Pages.Should().HaveCountGreaterThan(0,
            "super-sampled generation should produce atlas pages");

        // Atlas should contain non-zero pixels
        result.Pages[0].PixelData.Any(b => b != 0).Should().BeTrue(
            "super-sampled atlas should contain rendered glyph pixels");
    }

    [Fact]
    public void Generate_WithSuperSampling_MetricsAreNotDoubled()
    {
        // Arrange — verify that metrics don't accidentally remain at the upscaled size
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("ABCxyz");

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            SuperSampleLevel = 4
        });

        // Assert — no glyph should have dimensions approaching 4x the requested size.
        // At 32px with 4x super sampling, rasterization happens at 128px internally,
        // but the output should be back at ~32px scale.
        foreach (var ch in result.Model.Characters)
        {
            ch.Width.Should().BeLessThan(64,
                $"glyph {ch.Id} width should not be at the 4x upscaled size");
            ch.Height.Should().BeLessThan(64,
                $"glyph {ch.Id} height should not be at the 4x upscaled size");
            ch.XAdvance.Should().BeLessThan(64,
                $"glyph {ch.Id} XAdvance should not be at the 4x upscaled size");
        }
    }

    #endregion
}
