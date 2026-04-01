using KernSmith.Atlas;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Integration;

public class EndToEndTests
{
    public EndToEndTests()
    {
        if (!RasterizerFactory.GetAvailableBackends().Contains(RasterizerBackend.FreeType))
            RasterizerFactory.Register(RasterizerBackend.FreeType, () => new FreeTypeRasterizer());
    }

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
        result.Model.ShouldNotBeNull();
        result.Model.Info.Face.ShouldBe("Roboto");
        result.Model.Info.Size.ShouldBe(32);
        result.Model.Info.Unicode.ShouldBeTrue();

        // Should have characters (ASCII printable = 95 codepoints max)
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Model.Characters.Count.ShouldBeLessThanOrEqualTo(95);

        // Should have at least 1 atlas page
        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Model.Common.Pages.ShouldBe(result.Pages.Count);

        // Atlas dimensions should be positive
        result.Model.Common.ScaleW.ShouldBeGreaterThan(0);
        result.Model.Common.ScaleH.ShouldBeGreaterThan(0);

        // Each page should have pixel data matching atlas dimensions
        foreach (var page in result.Pages)
        {
            page.PixelData.ShouldNotBeEmpty();
            page.Width.ShouldBe(result.Model.Common.ScaleW);
            page.Height.ShouldBe(result.Model.Common.ScaleH);
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
        text.ShouldStartWith("info ");
        text.ShouldContain("face=\"Roboto\"");
        text.ShouldContain("common ");
        text.ShouldContain("page ");
        text.ShouldContain("chars count=");
        text.ShouldContain("char id=");
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
        hasNonZeroPixels.ShouldBeTrue("atlas should contain rendered glyph pixels");
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
        space.ShouldNotBeNull("ASCII set should include space (U+0020)");
        space!.XAdvance.ShouldBeGreaterThan(0);

        // 'A' character should have positive dimensions and advance
        var charA = result.Model.Characters.FirstOrDefault(c => c.Id == 65);
        charA.ShouldNotBeNull("ASCII set should include 'A' (U+0041)");
        charA!.Width.ShouldBeGreaterThan(0);
        charA!.Height.ShouldBeGreaterThan(0);
        charA!.XAdvance.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithSizeOverload_Works()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 16);

        // Assert
        result.Model.Info.Size.ShouldBe(16);
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
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
        result.Model.KerningPairs.Count.ShouldBeGreaterThan(0);
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
        pngBytes.ShouldNotBeEmpty();
        pngBytes[0].ShouldBe((byte)137);
        pngBytes[1].ShouldBe((byte)80);  // 'P'
        pngBytes[2].ShouldBe((byte)78);  // 'N'
        pngBytes[3].ShouldBe((byte)71);  // 'G'
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
        largeResult.Model.Common.LineHeight.ShouldBeGreaterThan(
            smallResult.Model.Common.LineHeight);
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
        result.Model.Characters.Count.ShouldBe(3);
        result.Model.Characters.Select(c => c.Id).ShouldBe(new[] { 65, 66, 67 });
    }

    [Fact]
    public void Generate_ModelPagesMatchAtlasPages()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, 32);

        // Assert — model page entries should match atlas page count
        result.Model.Pages.Count.ShouldBe(result.Pages.Count);

        // Page indices should be sequential starting from 0
        for (int i = 0; i < result.Model.Pages.Count; i++)
        {
            result.Model.Pages[i].Id.ShouldBe(i);
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
        xml.ShouldContain("<font>");
        xml.ShouldContain("face=\"Roboto\"");
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
        binary[0].ShouldBe((byte)66);
        binary[1].ShouldBe((byte)77);
        binary[2].ShouldBe((byte)70);
        binary[3].ShouldBe((byte)3);
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "generation with Skyline packer should produce characters");
        result.Pages.Count.ShouldBeGreaterThan(0,
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
        result.Model.ShouldNotBeNull();
        result.Model.Info.Size.ShouldBe(32);
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Pages.Count.ShouldBeGreaterThan(0);
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
        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);

        // Pixel data should be 4x the size of a grayscale atlas
        result.Pages[0].PixelData.Length.ShouldBe(
            result.Pages[0].Width * result.Pages[0].Height * 4);

        // Characters should have individual channel assignments (1, 2, 4, or 8), not 15
        result.Model.Characters.ShouldContain(c => c.Channel == 1 || c.Channel == 2 || c.Channel == 4 || c.Channel == 8);
        result.Model.Characters.ShouldNotContain(c => c.Channel == 15);

        // Common block should indicate packed
        result.Model.Common.Packed.ShouldBeTrue();
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
        result.Model.Common.Packed.ShouldBeTrue();
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Pages.Count.ShouldBeGreaterThan(0);

        // Atlas should contain non-zero pixel data (rendered glyphs present)
        result.Pages[0].PixelData.Any(b => b != 0).ShouldBeTrue(
            "gradient atlas should contain rendered pixels");

        // Model should be well-formed
        result.Model.Info.Face.ShouldBe("Roboto");
        result.Model.Info.Size.ShouldBe(32);
        result.Model.Common.LineHeight.ShouldBeGreaterThan(0);
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

        charWith.Width.ShouldBeGreaterThan(charWithout.Width,
            "outlined glyph should be wider than non-outlined glyph");
        charWith.Height.ShouldBeGreaterThan(charWithout.Height,
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

        charWith.XAdvance.ShouldBe(charWithout.XAdvance,
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

        charWith.XAdvance.ShouldBe(charWithout.XAdvance,
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

        charWith.Width.ShouldBeGreaterThan(charWithout.Width,
            "outlined glyph should be wider when using custom channel config");
        charWith.Height.ShouldBeGreaterThan(charWithout.Height,
            "outlined glyph should be taller when using custom channel config");
        charWith.XAdvance.ShouldBe(charWithout.XAdvance,
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

        hasOutlineBeyondGlyph.ShouldBeTrue(
            "outline channel should contain pixels beyond the glyph boundary — " +
            "if both channels are identical, the outline is invisible");
    }
}
