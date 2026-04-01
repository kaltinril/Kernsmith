using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for color font support (task 13B): FT_LOAD_COLOR, BGRA-to-RGBA swap,
/// RGBA atlas pages, HasColorGlyphs detection, channel packing guard, and
/// post-processor skip behavior for RGBA glyphs.
/// Some tests require a color font fixture (TTF with COLR/sbix/CBDT tables).
/// These are skipped if no color font is available.
/// </summary>
public class ColorFontTests : IDisposable
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(FixturesDir, "Roboto-Regular.ttf"));

    /// <summary>
    /// Attempts to find a color font (one with COLR, sbix, or CBDT tables) in the Fixtures directory.
    /// Returns null if none is available.
    /// </summary>
    private static byte[]? LoadColorFontOrNull()
    {
        if (!Directory.Exists(FixturesDir))
            return null;

        var fontReader = new TtfFontReader();
        foreach (var file in Directory.GetFiles(FixturesDir, "*.ttf")
                     .Concat(Directory.GetFiles(FixturesDir, "*.otf")))
        {
            try
            {
                var data = File.ReadAllBytes(file);
                var info = fontReader.ReadFont(data);
                if (info.HasColorGlyphs)
                    return data;
            }
            catch
            {
                // Skip files that fail to parse
            }
        }

        return null;
    }

    private readonly byte[]? _colorFontData = LoadColorFontOrNull();
    private FreeTypeRasterizer? _rasterizer;

    public void Dispose()
    {
        _rasterizer?.Dispose();
    }

    // ---------------------------------------------------------------
    // Default values
    // ---------------------------------------------------------------

    [Fact]
    public void FontGeneratorOptions_ColorFont_DefaultIsFalse()
    {
        // Arrange & Act
        var options = new FontGeneratorOptions();

        // Assert
        options.ColorFont.ShouldBeFalse("ColorFont should default to false");
    }

    [Fact]
    public void FontGeneratorOptions_ColorPaletteIndex_DefaultIsZero()
    {
        // Arrange & Act
        var options = new FontGeneratorOptions();

        // Assert
        options.ColorPaletteIndex.ShouldBe(0);
    }

    [Fact]
    public void RasterOptions_ColorFont_DefaultIsFalse()
    {
        // Arrange & Act
        var rasterOptions = new RasterOptions { Size = 32 };

        // Assert
        rasterOptions.ColorFont.ShouldBeFalse("ColorFont should default to false");
    }

    [Fact]
    public void RasterOptions_ColorPaletteIndex_DefaultIsZero()
    {
        // Arrange & Act
        var rasterOptions = new RasterOptions { Size = 32 };

        // Assert
        rasterOptions.ColorPaletteIndex.ShouldBe(0);
    }

    [Fact]
    public void RasterOptions_FromGeneratorOptions_CopiesColorFontSettings()
    {
        // Arrange
        var generatorOptions = new FontGeneratorOptions
        {
            Size = 32,
            ColorFont = true,
            ColorPaletteIndex = 3
        };

        // Act
        var rasterOptions = RasterOptions.FromGeneratorOptions(generatorOptions);

        // Assert
        rasterOptions.ColorFont.ShouldBeTrue("ColorFont should be propagated from generator options");
        rasterOptions.ColorPaletteIndex.ShouldBe(3);
    }

    // ---------------------------------------------------------------
    // Builder fluent methods
    // ---------------------------------------------------------------

    [Fact]
    public void Builder_WithColorFont_SetsColorFontOption()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act -- WithColorFont(true) should set the option and produce a valid result
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.FromChars("A"))
            .WithColorFont()
            .Build();

        // Assert -- should succeed (Roboto is not a color font, but ColorFont=true is a graceful no-op)
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(1);
    }

    [Fact]
    public void Builder_WithColorFont_False_SetsColorFontOptionToFalse()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.FromChars("A"))
            .WithColorFont(false)
            .Build();

        // Assert
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(1);
    }

    [Fact]
    public void Builder_WithColorPaletteIndex_SetsOption()
    {
        // Arrange -- on a non-color font, palette index 0 is the default and does not
        // trigger SelectColorPalette (guarded by ColorPaletteIndex != 0 in BmFont.cs).
        // Non-zero palette indices would correctly fail on a non-color font, so we only
        // verify the builder accepts the method and palette 0 works end-to-end.
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.FromChars("A"))
            .WithColorFont()
            .WithColorPaletteIndex(0)
            .Build();

        // Assert -- should complete without error
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(1);
    }

    [Fact]
    public void Builder_WithNonZeroColorPaletteIndex_OnNonColorFont_Throws()
    {
        // Arrange -- Roboto has no CPAL table, so a non-zero palette index should fail
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.FromChars("A"))
            .WithColorFont()
            .WithColorPaletteIndex(2)
            .Build();

        // Assert -- FreeType rejects palette selection on fonts without CPAL tables
        Should.Throw<Exception>(act);
    }

    // ---------------------------------------------------------------
    // HasColorGlyphs detection
    // ---------------------------------------------------------------

    [Fact]
    public void FontInfo_NonColorFont_HasColorGlyphsIsFalse()
    {
        // Arrange
        var fontData = LoadTestFont();
        var fontReader = new TtfFontReader();

        // Act
        var fontInfo = fontReader.ReadFont(fontData);

        // Assert
        fontInfo.HasColorGlyphs.ShouldBeFalse(
            "Roboto-Regular does not contain COLR, sbix, or CBDT tables");
    }

    // ---------------------------------------------------------------
    // Non-color font with ColorFont=true (graceful fallback)
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_NonColorFont_WithColorFontTrue_ProducesValidResult()
    {
        // Arrange -- Roboto-Regular has no color glyphs, but ColorFont=true should not fail
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("AB"),
            ColorFont = true
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert -- should produce a valid result; FreeType falls back to grayscale rendering
        result.Model.ShouldNotBeNull();
        result.Model.Characters.Count.ShouldBe(2);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_NonColorFont_WithColorFontFalse_ProducesGrayscaleOutput()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            ColorFont = false
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert -- default (non-color) rendering should produce grayscale pages (1 bpp)
        result.Pages.Count.ShouldBeGreaterThan(0);
        foreach (var page in result.Pages)
        {
            page.Format.ShouldBe(PixelFormat.Grayscale8,
                "non-color font without ColorFont should produce grayscale atlas pages");
            page.PixelData.Length.ShouldBe(page.Width * page.Height,
                "grayscale pages should have 1 byte per pixel");
        }
    }

    // ---------------------------------------------------------------
    // Channel packing + ColorFont guard
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_ChannelPacking_WithColorFont_ThrowsInvalidOperationException()
    {
        // Arrange
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            ChannelPacking = true,
            ColorFont = true
        };

        // Act
        var act = () => BmFont.Generate(fontData, options);

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Channel packing");
    }

    [Fact]
    public void Builder_ChannelPacking_WithColorFont_ThrowsInvalidOperationException()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Builder()
            .WithFont(fontData)
            .WithSize(24)
            .WithCharacters(CharacterSet.FromChars("A"))
            .WithChannelPacking()
            .WithColorFont()
            .Build();

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Channel packing");
    }

    // ---------------------------------------------------------------
    // GradientPostProcessor skips RGBA glyphs
    // ---------------------------------------------------------------

    [Fact]
    public void GradientPostProcessor_SkipsRgbaGlyph()
    {
        // Arrange -- create a synthetic RGBA glyph
        var rgbaGlyph = CreateSyntheticRgbaGlyph();
        var processor = new GradientPostProcessor(255, 0, 0, 0, 0, 255);

        // Act
        var result = processor.Process(rgbaGlyph);

        // Assert -- should return the same glyph unmodified
        result.ShouldBeSameAs(rgbaGlyph,
            "GradientPostProcessor should return the same RGBA glyph instance without modification");
        result.Format.ShouldBe(PixelFormat.Rgba32);
        result.BitmapData.ShouldBeSameAs(rgbaGlyph.BitmapData);
    }

    [Fact]
    public void GradientPostProcessor_ProcessesGrayscaleGlyph()
    {
        // Arrange -- create a synthetic grayscale glyph
        var grayscaleGlyph = CreateSyntheticGrayscaleGlyph();
        var processor = new GradientPostProcessor(255, 0, 0, 0, 0, 255);

        // Act
        var result = processor.Process(grayscaleGlyph);

        // Assert -- should convert to RGBA
        result.ShouldNotBeSameAs(grayscaleGlyph,
            "GradientPostProcessor should create a new RGBA glyph from a grayscale input");
        result.Format.ShouldBe(PixelFormat.Rgba32);
        result.BitmapData.Length.ShouldBe(grayscaleGlyph.Width * grayscaleGlyph.Height * 4,
            "gradient output should be 4 bytes per pixel");
    }

    // ---------------------------------------------------------------
    // OutlinePostProcessor handles both RGBA and grayscale glyphs
    // ---------------------------------------------------------------

    [Fact]
    public void OutlinePostProcessor_ProcessesRgbaGlyph()
    {
        // Arrange -- create a synthetic RGBA glyph
        var rgbaGlyph = CreateSyntheticRgbaGlyph();
        var processor = new OutlinePostProcessor(2);

        // Act
        var result = processor.Process(rgbaGlyph);

        // Assert -- should expand the glyph with an outline (RGBA output)
        result.ShouldNotBeSameAs(rgbaGlyph,
            "OutlinePostProcessor should create a new expanded glyph from an RGBA input");
        result.Format.ShouldBe(PixelFormat.Rgba32);
        result.Width.ShouldBe(rgbaGlyph.Width + 2 * 2,
            "outlined glyph should be wider by 2 * outlineWidth");
        result.Height.ShouldBe(rgbaGlyph.Height + 2 * 2,
            "outlined glyph should be taller by 2 * outlineWidth");
    }

    [Fact]
    public void OutlinePostProcessor_ProcessesGrayscaleGlyph()
    {
        // Arrange -- create a synthetic grayscale glyph with some non-zero pixels
        var grayscaleGlyph = CreateSyntheticGrayscaleGlyph();
        var processor = new OutlinePostProcessor(2);

        // Act
        var result = processor.Process(grayscaleGlyph);

        // Assert -- should expand the glyph with an outline (now outputs RGBA)
        result.ShouldNotBeSameAs(grayscaleGlyph,
            "OutlinePostProcessor should create a new expanded glyph from a grayscale input");
        result.Format.ShouldBe(PixelFormat.Rgba32,
            "OutlinePostProcessor now always outputs RGBA with baked outline color");
        result.Width.ShouldBe(grayscaleGlyph.Width + 2 * 2,
            "outlined glyph should be wider by 2 * outlineWidth");
        result.Height.ShouldBe(grayscaleGlyph.Height + 2 * 2,
            "outlined glyph should be taller by 2 * outlineWidth");
    }

    // ---------------------------------------------------------------
    // SelectColorPalette guard
    // ---------------------------------------------------------------

    [Fact]
    public void SelectColorPalette_WithoutLoadFont_ThrowsInvalidOperationException()
    {
        // Arrange
        _rasterizer = new FreeTypeRasterizer();

        // Act
        var act = () => _rasterizer.SelectColorPalette(0);

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Font not loaded");
    }

    // ---------------------------------------------------------------
    // Integration: color font rendering (full pipeline)
    // ---------------------------------------------------------------

    [Fact]
    public void Generate_NonColorFont_WithColorFont_ProducesSameCharacterCount()
    {
        // Arrange -- compare ColorFont=true vs ColorFont=false on a non-color font
        var fontData = LoadTestFont();
        var chars = CharacterSet.FromChars("Hello");

        var optionsColor = new FontGeneratorOptions
        {
            Size = 24,
            Characters = chars,
            ColorFont = true
        };
        var optionsGrayscale = new FontGeneratorOptions
        {
            Size = 24,
            Characters = chars,
            ColorFont = false
        };

        // Act
        var resultColor = BmFont.Generate(fontData, optionsColor);
        var resultGrayscale = BmFont.Generate(fontData, optionsGrayscale);

        // Assert -- both should produce the same number of characters
        resultColor.Model.Characters.Count.ShouldBe(resultGrayscale.Model.Characters.Count,
            "ColorFont=true on a non-color font should not change the character count");
    }

    // ---------------------------------------------------------------
    // Color font tests (skipped if no color font fixture exists)
    // ---------------------------------------------------------------

    [Fact]
    public void FontInfo_ColorFont_HasColorGlyphsIsTrue()
    {
        // Arrange
        var fontData = _colorFontData!;
        var fontReader = new TtfFontReader();

        // Act
        var fontInfo = fontReader.ReadFont(fontData);

        // Assert
        fontInfo.HasColorGlyphs.ShouldBeTrue(
            "a color font should have COLR, sbix, or CBDT tables detected");
    }

    [Fact]
    public void Generate_ColorFont_WithColorFontTrue_ProducesRgbaAtlas()
    {
        // Arrange -- use emoji codepoints since NotoColorEmoji does not contain ASCII glyphs.
        // U+1F600 = grinning face, U+2764 = red heart, U+2B50 = star
        var fontData = _colorFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x2764, 0x2B50 }),
            ColorFont = true
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert -- atlas pages should be RGBA (4 bytes per pixel)
        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32,
            "color font rendering should produce RGBA atlas pages");
        result.Pages[0].PixelData.Length.ShouldBe(
            result.Pages[0].Width * result.Pages[0].Height * 4,
            "RGBA pages should have 4 bytes per pixel");
    }

    [Fact]
    public void Generate_ColorFont_WithColorFontTrue_AtlasContainsColorData()
    {
        // Arrange -- use emoji codepoints since NotoColorEmoji does not contain ASCII glyphs
        var fontData = _colorFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x1F600, 0x2764, 0x2B50 }),
            ColorFont = true
        };

        // Act
        var result = BmFont.Generate(fontData, options);

        // Assert -- at least some pixels should have non-white RGB values (actual color data)
        var pixelData = result.Pages[0].PixelData;
        var hasColorPixels = false;
        for (var i = 0; i < pixelData.Length - 3; i += 4)
        {
            var r = pixelData[i];
            var g = pixelData[i + 1];
            var b = pixelData[i + 2];
            var a = pixelData[i + 3];
            if (a > 0 && (r != g || g != b))
            {
                hasColorPixels = true;
                break;
            }
        }

        hasColorPixels.ShouldBeTrue(
            "color font atlas should contain pixels with varying RGB channels");
    }

    [Fact]
    public void Generate_ColorFont_WithColorPaletteIndex_DoesNotThrow()
    {
        // Arrange -- use emoji codepoints since NotoColorEmoji does not contain ASCII glyphs
        var fontData = _colorFontData!;
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x1F600 }),
            ColorFont = true,
            ColorPaletteIndex = 0
        };

        // Act
        var act = () => BmFont.Generate(fontData, options);

        // Assert
        Should.NotThrow(act); // palette index 0 should always be valid for a color font
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static RasterizedGlyph CreateSyntheticRgbaGlyph()
    {
        const int width = 10;
        const int height = 10;
        var bitmapData = new byte[width * height * 4];

        // Fill with a recognizable color pattern (red with full alpha)
        for (var i = 0; i < bitmapData.Length; i += 4)
        {
            bitmapData[i + 0] = 255; // R
            bitmapData[i + 1] = 0;   // G
            bitmapData[i + 2] = 0;   // B
            bitmapData[i + 3] = 255; // A
        }

        return new RasterizedGlyph
        {
            Codepoint = 65, // 'A'
            GlyphIndex = 1,
            BitmapData = bitmapData,
            Width = width,
            Height = height,
            Pitch = width * 4,
            Metrics = new GlyphMetrics(BearingX: 1, BearingY: 10, Advance: 12, Width: width, Height: height),
            Format = PixelFormat.Rgba32
        };
    }

    private static RasterizedGlyph CreateSyntheticGrayscaleGlyph()
    {
        const int width = 10;
        const int height = 10;
        var bitmapData = new byte[width * height];

        // Fill center pixels with non-zero alpha to simulate a rendered glyph
        for (var y = 2; y < 8; y++)
        {
            for (var x = 2; x < 8; x++)
            {
                bitmapData[y * width + x] = 200;
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = 65, // 'A'
            GlyphIndex = 1,
            BitmapData = bitmapData,
            Width = width,
            Height = height,
            Pitch = width,
            Metrics = new GlyphMetrics(BearingX: 1, BearingY: 10, Advance: 12, Width: width, Height: height),
            Format = PixelFormat.Grayscale8
        };
    }
}
