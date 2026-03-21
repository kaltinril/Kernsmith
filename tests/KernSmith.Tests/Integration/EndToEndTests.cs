using KernSmith.Atlas;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using FluentAssertions;

namespace KernSmith.Tests.Integration;

public class EndToEndTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void Generate_AsciiFont_ProducesValidResult()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        });

        // Assert — model should be populated
        result.Model.Should().NotBeNull();
        result.Model.Info.Face.Should().Be("Roboto");
        result.Model.Info.Size.Should().Be(32);
        result.Model.Info.Unicode.Should().BeTrue();

        // Should have characters (ASCII printable = 95 codepoints max)
        result.Model.Characters.Should().HaveCountGreaterThan(0);
        result.Model.Characters.Count.Should().BeLessThanOrEqualTo(95);

        // Should have at least 1 atlas page
        result.Pages.Should().HaveCountGreaterThan(0);
        result.Model.Common.Pages.Should().Be(result.Pages.Count);

        // Atlas dimensions should be positive
        result.Model.Common.ScaleW.Should().BeGreaterThan(0);
        result.Model.Common.ScaleH.Should().BeGreaterThan(0);

        // Each page should have pixel data matching atlas dimensions
        foreach (var page in result.Pages)
        {
            page.PixelData.Should().NotBeEmpty();
            page.Width.Should().Be(result.Model.Common.ScaleW);
            page.Height.Should().Be(result.Model.Common.ScaleH);
        }
    }

    [Fact]
    public void Generate_ProducesValidTextFormat()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);
        var text = result.ToString();

        // Assert — text output follows BMFont text format
        text.Should().StartWith("info ");
        text.Should().Contain("face=\"Roboto\"");
        text.Should().Contain("common ");
        text.Should().Contain("page ");
        text.Should().Contain("chars count=");
        text.Should().Contain("char id=");
    }

    [Fact]
    public void Generate_AtlasPagesContainNonZeroPixels()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);

        // Assert — at least one page should have rendered glyph pixels
        var hasNonZeroPixels = result.Pages.Any(p => p.PixelData.Any(b => b != 0));
        hasNonZeroPixels.Should().BeTrue("atlas should contain rendered glyph pixels");
    }

    [Fact]
    public void Generate_CharacterMetricsAreReasonable()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);

        // Assert — space character should have positive advance
        var space = result.Model.Characters.FirstOrDefault(c => c.Id == 32);
        space.Should().NotBeNull("ASCII set should include space (U+0020)");
        space!.XAdvance.Should().BeGreaterThan(0, "space should have positive advance");

        // 'A' character should have positive dimensions and advance
        var charA = result.Model.Characters.FirstOrDefault(c => c.Id == 65);
        charA.Should().NotBeNull("ASCII set should include 'A' (U+0041)");
        charA!.Width.Should().BeGreaterThan(0);
        charA!.Height.Should().BeGreaterThan(0);
        charA!.XAdvance.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithSizeOverload_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 16);

        // Assert
        result.Model.Info.Size.Should().Be(16);
        result.Model.Characters.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Generate_KerningPairsExist()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Kerning = true
        });

        // Assert — Roboto has kerning pairs
        result.Model.KerningPairs.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Generate_ToPng_ProducesValidPngBytes()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);
        var pngBytes = result.Pages[0].ToPng();

        // Assert — PNG magic bytes: 137 80 78 71 13 10 26 10
        pngBytes.Should().NotBeEmpty();
        pngBytes[0].Should().Be(137);
        pngBytes[1].Should().Be(80);  // 'P'
        pngBytes[2].Should().Be(78);  // 'N'
        pngBytes[3].Should().Be(71);  // 'G'
    }

    [Fact]
    public void Generate_DifferentSizes_ProduceDifferentLineHeights()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var smallResult = BmFont.Generate(fontData, 12);
        var largeResult = BmFont.Generate(fontData, 48);

        // Assert — larger size should produce larger line height
        largeResult.Model.Common.LineHeight.Should()
            .BeGreaterThan(smallResult.Model.Common.LineHeight,
                "48pt font should have larger line height than 12pt font");
    }

    [Fact]
    public void Generate_FromString_ProducesOnlyRequestedCharacters()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("ABC");

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars
        });

        // Assert — should have exactly 3 characters
        result.Model.Characters.Should().HaveCount(3);
        result.Model.Characters.Select(c => c.Id).Should().BeEquivalentTo(new[] { 65, 66, 67 });
    }

    [Fact]
    public void Generate_ModelPagesMatchAtlasPages()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);

        // Assert — model page entries should match atlas page count
        result.Model.Pages.Should().HaveCount(result.Pages.Count);

        // Page indices should be sequential starting from 0
        for (int i = 0; i < result.Model.Pages.Count; i++)
        {
            result.Model.Pages[i].Id.Should().Be(i);
        }
    }

    [Fact]
    public void Generate_ToXml_ProducesValidXml()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);
        var xml = result.ToXml();

        // Assert
        xml.Should().Contain("<font>", "XML output should contain the font root element");
        xml.Should().Contain("face=\"Roboto\"", "XML output should contain the font face name");
    }

    [Fact]
    public void Generate_ToBinary_ProducesValidBinary()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);
        var binary = result.ToBinary();

        // Assert — BMF header + version 3
        binary[0].Should().Be(66, "first byte should be 'B'");
        binary[1].Should().Be(77, "second byte should be 'M'");
        binary[2].Should().Be(70, "third byte should be 'F'");
        binary[3].Should().Be(3, "fourth byte should be version 3");
    }

    [Fact]
    public void Generate_WithSkylinePacker_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Packer = new SkylinePacker()
        });

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "generation with Skyline packer should produce characters");
        result.Pages.Should().HaveCountGreaterThan(0,
            "generation with Skyline packer should produce atlas pages");
    }

    [Fact]
    public void Generate_WithBuilder_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(32)
            .Build();

        // Assert
        result.Model.Should().NotBeNull();
        result.Model.Info.Size.Should().Be(32);
        result.Model.Characters.Should().HaveCountGreaterThan(0);
        result.Pages.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Generate_WithChannelPacking_ProducesRgbaAtlas()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            ChannelPacking = true
        });

        // Assert — Atlas pages should be RGBA
        result.Pages[0].Format.Should().Be(PixelFormat.Rgba32);

        // Pixel data should be 4x the size of a grayscale atlas
        result.Pages[0].PixelData.Length.Should().Be(
            result.Pages[0].Width * result.Pages[0].Height * 4);

        // Characters should have individual channel assignments (1, 2, 4, or 8), not 15
        result.Model.Characters.Should().Contain(c => c.Channel == 1 || c.Channel == 2 || c.Channel == 4 || c.Channel == 8);
        result.Model.Characters.Should().NotContain(c => c.Channel == 15);

        // Common block should indicate packed
        result.Model.Common.Packed.Should().BeTrue();
    }

    [Fact]
    public void Builder_WithChannelPacking_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.Ascii)
            .WithChannelPacking()
            .Build();

        // Assert
        result.Model.Common.Packed.Should().BeTrue();
        result.Model.Characters.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Builder_WithSkylinePacker_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(32)
            .WithPackingAlgorithm(PackingAlgorithm.Skyline)
            .Build();

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void Generate_WithGradient_ProducesValidOutput()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(32)
            .WithCharacters(CharacterSet.Ascii)
            .WithGradient((255, 0, 0), (0, 0, 255))  // red -> blue
            .Build();

        // Assert -- generation should succeed with gradient post-processor
        result.Model.Characters.Should().HaveCountGreaterThan(0);
        result.Pages.Should().HaveCountGreaterThan(0);

        // Atlas should contain non-zero pixel data (rendered glyphs present)
        result.Pages[0].PixelData.Any(b => b != 0).Should().BeTrue(
            "gradient atlas should contain rendered pixels");

        // Model should be well-formed
        result.Model.Info.Face.Should().Be("Roboto");
        result.Model.Info.Size.Should().Be(32);
        result.Model.Common.LineHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithOutline_ProducesLargerGlyphs()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            PostProcessors = new[] { new OutlinePostProcessor(3) }
        });

        // Assert — outlined glyph should be larger (expanded by 2*outline in each dimension)
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.Width.Should().BeGreaterThan(charWithout.Width,
            "outlined glyph should be wider than non-outlined glyph");
        charWith.Height.Should().BeGreaterThan(charWithout.Height,
            "outlined glyph should be taller than non-outlined glyph");
    }

    [Fact]
    public void Generate_WithOutlineProperty_AdvanceIsUnchanged()
    {
        // Arrange — outline expands the glyph bitmap and bearing but should NOT
        // change the advance. The outline is allowed to overlap into adjacent space.
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            Outline = 4
        });

        // Assert
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.XAdvance.Should().Be(charWithout.XAdvance,
            "outline should not change the advance — outlines overlap into adjacent glyph space");
    }

    [Fact]
    public void Generate_WithOutlinePostProcessor_AdvanceIsUnchanged()
    {
        // Arrange
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            PostProcessors = new[] { new OutlinePostProcessor(4) }
        });

        // Assert
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.XAdvance.Should().Be(charWithout.XAdvance,
            "outline should not change the advance — outlines overlap into adjacent glyph space");
    }

    [Fact]
    public void Generate_WithOutlineAndCustomChannels_ExpandsMetrics()
    {
        // Arrange — mimics Gum's outline channel layout:
        // alpha=outline, RGB=glyph (white text with outline in alpha)
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");
        var outlineWidth = 4;

        // Act
        var resultWithout = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Glyph,
                Red: ChannelContent.One,
                Green: ChannelContent.One,
                Blue: ChannelContent.One)
        });

        var resultWith = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = chars,
            Outline = outlineWidth,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Outline,
                Red: ChannelContent.Glyph,
                Green: ChannelContent.Glyph,
                Blue: ChannelContent.Glyph)
        });

        // Assert — metrics must reflect the outline expansion
        var charWithout = resultWithout.Model.Characters.First(c => c.Id == 65);
        var charWith = resultWith.Model.Characters.First(c => c.Id == 65);

        charWith.Width.Should().BeGreaterThan(charWithout.Width,
            "outlined glyph should be wider when using custom channel config");
        charWith.Height.Should().BeGreaterThan(charWithout.Height,
            "outlined glyph should be taller when using custom channel config");
        charWith.XAdvance.Should().Be(charWithout.XAdvance,
            "outline should not change the advance — outlines overlap into adjacent glyph space");
    }

    [Fact]
    public void Generate_WithOutlineAndCustomChannels_GlyphChannelDiffersFromOutlineChannel()
    {
        // Arrange — verify the atlas has different data in glyph vs outline channels,
        // not the same data in both (which would make outlines invisible).
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("A");

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 48,
            Characters = chars,
            Outline = 3,
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Outline,
                Red: ChannelContent.Glyph,
                Green: ChannelContent.Glyph,
                Blue: ChannelContent.Glyph)
        });

        // Assert — the outline (alpha channel) should have non-zero pixels in regions
        // where the glyph channel (red) is zero, because the outline extends beyond the glyph.
        var page = result.Pages[0];
        var charEntry = result.Model.Characters.First(c => c.Id == 65);
        var hasOutlineBeyondGlyph = false;

        for (var y = charEntry.Y; y < charEntry.Y + charEntry.Height && y < page.Height; y++)
        {
            for (var x = charEntry.X; x < charEntry.X + charEntry.Width && x < page.Width; x++)
            {
                var idx = (y * page.Width + x) * 4;
                var red = page.PixelData[idx + 0];   // glyph channel
                var alpha = page.PixelData[idx + 3]; // outline channel

                if (alpha > 0 && red == 0)
                {
                    // Outline pixel exists where glyph pixel doesn't — this is the outline ring
                    hasOutlineBeyondGlyph = true;
                    break;
                }
            }
            if (hasOutlineBeyondGlyph) break;
        }

        hasOutlineBeyondGlyph.Should().BeTrue(
            "outline channel should contain pixels beyond the glyph boundary — " +
            "if both channels are identical, the outline is invisible");
    }
}
