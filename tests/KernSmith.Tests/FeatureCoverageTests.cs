using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Track B3 — Feature coverage tests from Phase 12 pre-ship polish plan.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class FeatureCoverageTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ------------------------------------------------------------------
    // B3.1 — SDF generation
    // ------------------------------------------------------------------

    [Fact]
    public void Generate_WithSdf_ProducesNonEmptyOutput()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABCabc"),
            Sdf = true
        });

        // Assert
        result.Model.ShouldNotBeNull();
        result.Pages.Count.ShouldBeGreaterThan(0);
        result.Model.Characters.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithSdf_AtlasPagesContainNonZeroPixels()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABCabc"),
            Sdf = true
        });

        // Assert
        var hasNonZeroPixels = result.Pages.Any(p => p.PixelData.Any(b => b != 0));
        hasNonZeroPixels.ShouldBeTrue("SDF atlas should contain rendered distance field pixels");
    }

    [Fact]
    public void Generate_WithSdf_CharacterDimensionsArePositive()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A"),
            Sdf = true
        });

        // Assert — the 'A' glyph should have positive dimensions in SDF mode
        var charA = result.Model.Characters.FirstOrDefault(c => c.Id == 65);
        charA.ShouldNotBeNull("SDF output should include character 'A'");
        charA!.Width.ShouldBeGreaterThan(0);
        charA!.Height.ShouldBeGreaterThan(0);
        charA!.XAdvance.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithSdf_AndSuperSample_ThrowsInvalidOperation()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var act = () => BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Sdf = true,
            SuperSampleLevel = 2
        });

        // Assert
        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("SDF");
    }

    // ------------------------------------------------------------------
    // B3.2 — Multi-page atlas
    // ------------------------------------------------------------------

    [Fact]
    public void Generate_SmallTextureWithLargeCharset_ProducesMultiplePages()
    {
        // Arrange — use ASCII charset with very small texture to force multiple pages
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            MaxTextureWidth = 64,
            MaxTextureHeight = 64,
            PowerOfTwo = true
        });

        // Assert
        result.Pages.Count.ShouldBeGreaterThan(1,
            "a 64x64 texture should not fit all ASCII characters at size 32, requiring multiple pages");
        result.Model.Common.Pages.ShouldBe(result.Pages.Count,
            "model page count should match actual atlas page count");
    }

    [Fact]
    public void Generate_MultiPage_CharactersHaveValidPageAssignments()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            MaxTextureWidth = 64,
            MaxTextureHeight = 64,
            PowerOfTwo = true
        });

        // Assert — every character's page index should be within the valid range
        var pageCount = result.Pages.Count;
        foreach (var ch in result.Model.Characters)
        {
            ch.Page.ShouldBeGreaterThanOrEqualTo(0,
                $"character U+{ch.Id:X4} should have non-negative page index");
            ch.Page.ShouldBeLessThan(pageCount,
                $"character U+{ch.Id:X4} page index {ch.Page} should be less than total page count {pageCount}");
        }
    }

    [Fact]
    public void Generate_MultiPage_AllPagesContainPixelData()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            MaxTextureWidth = 64,
            MaxTextureHeight = 64,
            PowerOfTwo = true
        });

        // Assert — each page should have non-empty pixel data
        for (var i = 0; i < result.Pages.Count; i++)
        {
            result.Pages[i].PixelData.ShouldNotBeEmpty(
                $"atlas page {i} should have pixel data");
        }
    }

    [Fact]
    public void Generate_MultiPage_ModelPagesAreSequential()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            MaxTextureWidth = 64,
            MaxTextureHeight = 64,
            PowerOfTwo = true
        });

        // Assert — model page entries should have sequential IDs starting from 0
        result.Model.Pages.Count.ShouldBe(result.Pages.Count);
        for (var i = 0; i < result.Model.Pages.Count; i++)
        {
            result.Model.Pages[i].Id.ShouldBe(i,
                $"page entry at index {i} should have Id={i}");
        }
    }

    // ------------------------------------------------------------------
    // B3.3 — EDT (Euclidean Distance Transform) unit test
    // ------------------------------------------------------------------

    [Fact]
    public void Edt_SingleInsidePixelAtCenter_ProducesCorrectDistances()
    {
        // Arrange — 5x5 image with a single "inside" pixel at (2,2)
        var width = 5;
        var height = 5;
        var alpha = new byte[width * height];
        alpha[2 * width + 2] = 255; // pixel at (2,2) is inside

        // Act
        var distances = EuclideanDistanceTransform.Compute(alpha, width, height);

        // Assert — distance at center should be 0 (inside pixel)
        distances[2 * width + 2].ShouldBe(0f,
            "the inside pixel itself should have squared distance 0");

        // Adjacent pixels (distance = 1) should have squared distance = 1
        distances[1 * width + 2].ShouldBe(1f, 0.01f,
            "pixel one step above center should have squared distance 1");
        distances[3 * width + 2].ShouldBe(1f, 0.01f,
            "pixel one step below center should have squared distance 1");
        distances[2 * width + 1].ShouldBe(1f, 0.01f,
            "pixel one step left of center should have squared distance 1");
        distances[2 * width + 3].ShouldBe(1f, 0.01f,
            "pixel one step right of center should have squared distance 1");

        // Diagonal pixels (distance = sqrt(2)) should have squared distance = 2
        distances[1 * width + 1].ShouldBe(2f, 0.01f,
            "pixel diagonally adjacent should have squared distance 2");
        distances[1 * width + 3].ShouldBe(2f, 0.01f,
            "pixel diagonally adjacent should have squared distance 2");
        distances[3 * width + 1].ShouldBe(2f, 0.01f,
            "pixel diagonally adjacent should have squared distance 2");
        distances[3 * width + 3].ShouldBe(2f, 0.01f,
            "pixel diagonally adjacent should have squared distance 2");

        // Corner pixels at (0,0): distance = sqrt(2^2 + 2^2) = sqrt(8), squared = 8
        distances[0 * width + 0].ShouldBe(8f, 0.01f,
            "corner pixel should have squared distance 8 from center");
    }

    [Fact]
    public void Edt_AllInsidePixels_AllDistancesAreZero()
    {
        // Arrange — 3x3 image where every pixel is inside
        var width = 3;
        var height = 3;
        var alpha = new byte[width * height];
        Array.Fill(alpha, (byte)255);

        // Act
        var distances = EuclideanDistanceTransform.Compute(alpha, width, height);

        // Assert — all distances should be 0
        foreach (var d in distances)
        {
            d.ShouldBe(0f, "all pixels are inside, so distance should be 0");
        }
    }

    [Fact]
    public void Edt_AllOutsidePixels_AllDistancesAreVeryLarge()
    {
        // Arrange — 3x3 image where every pixel is outside (alpha = 0)
        var width = 3;
        var height = 3;
        var alpha = new byte[width * height];

        // Act
        var distances = EuclideanDistanceTransform.Compute(alpha, width, height);

        // Assert — with no inside pixels, all distances should be very large (infinity marker)
        foreach (var d in distances)
        {
            d.ShouldBeGreaterThan(1_000_000f,
                "with no inside pixels, distances should remain at the infinity sentinel");
        }
    }

    [Fact]
    public void Edt_HorizontalLine_ProducesSymmetricDistances()
    {
        // Arrange — 5x3 image with a horizontal line of inside pixels at row 1
        var width = 5;
        var height = 3;
        var alpha = new byte[width * height];
        for (var x = 0; x < width; x++)
            alpha[1 * width + x] = 255; // row 1 is all inside

        // Act
        var distances = EuclideanDistanceTransform.Compute(alpha, width, height);

        // Assert — row 0 and row 2 should have squared distance 1 from the inside line
        for (var x = 0; x < width; x++)
        {
            distances[1 * width + x].ShouldBe(0f,
                $"inside pixel at ({x},1) should have distance 0");
            distances[0 * width + x].ShouldBe(1f, 0.01f,
                $"pixel at ({x},0) should be 1 away from the inside line");
            distances[2 * width + x].ShouldBe(1f, 0.01f,
                $"pixel at ({x},2) should be 1 away from the inside line");
        }
    }

    // ------------------------------------------------------------------
    // B3.4 — Corrupted .fnt input
    // ------------------------------------------------------------------

    [Fact]
    public void LoadModel_TruncatedBinaryData_ThrowsFormatException()
    {
        // Arrange — valid BMF header but truncated block data
        var truncated = new byte[] { 66, 77, 70, 3, 1, 0xFF, 0, 0, 0 };

        // Act
        var act = () => BmFont.LoadModel(truncated);

        // Assert — should throw a FormatException, not crash or hang
        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void LoadModel_EmptyBinaryData_ThrowsFormatException()
    {
        // Arrange — empty data falls through to text format parsing in BmFontReader.Read
        var empty = Array.Empty<byte>();

        // Act
        var act = () => BmFont.LoadModel(empty);

        // Assert — should throw FormatException regardless of which format path is taken
        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void LoadModel_RandomGarbageBytes_ThrowsFormatException()
    {
        // Arrange — random bytes that do not match any known format
        var garbage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33 };

        // Act — BmFontReader.Read will fall through to text format parsing
        var act = () => BmFont.LoadModel(garbage);

        // Assert — should throw a parsing error, not crash
        Should.Throw<FormatException>(act);
    }

    [Fact]
    public void LoadModel_TextMissingRequiredInfoLine_ThrowsFormatException()
    {
        // Arrange — text format with common line but no info line
        var badText = "common lineHeight=38 base=30 scaleW=256 scaleH=256 pages=1 packed=0\n" +
                      "page id=0 file=\"font_0.png\"\n" +
                      "chars count=1\n" +
                      "char id=65 x=0 y=0 width=20 height=25 xoffset=0 yoffset=5 xadvance=22 page=0 chnl=15\n";

        // Act
        var act = () => BmFont.LoadModel(badText);

        // Assert
        var ex = Should.Throw<FormatException>(act);
        ex.Message.ShouldContain("missing");
    }

    [Fact]
    public void LoadModel_TextMissingRequiredCommonLine_ThrowsFormatException()
    {
        // Arrange — text format with info line but no common line
        var badText = "info face=\"Test\" size=32 bold=0 italic=0 charset=\"\" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=0,0\n" +
                      "page id=0 file=\"font_0.png\"\n" +
                      "chars count=1\n" +
                      "char id=65 x=0 y=0 width=20 height=25 xoffset=0 yoffset=5 xadvance=22 page=0 chnl=15\n";

        // Act
        var act = () => BmFont.LoadModel(badText);

        // Assert
        var ex = Should.Throw<FormatException>(act);
        ex.Message.ShouldContain("missing");
    }

    [Fact]
    public void LoadModel_BinaryBlockExceedsDataLength_ThrowsFormatException()
    {
        // Arrange — BMF header + block type 1 claiming size much larger than available data
        var corrupt = new byte[]
        {
            66, 77, 70, 3,    // BMF header v3
            1,                 // block type 1 (info)
            0xFF, 0x00, 0x00, 0x00  // block size = 255, but data ends here
        };

        // Act
        var act = () => BmFont.LoadModel(corrupt);

        // Assert
        var ex = Should.Throw<FormatException>(act);
        ex.Message.ShouldContain("exceeds");
    }

    // ------------------------------------------------------------------
    // B3.5 — WOFF2 unsupported
    // ------------------------------------------------------------------

    [Fact]
    public void WoffDecompressor_Woff2Data_ThrowsNotSupportedException()
    {
        // Arrange — minimal data with WOFF2 signature "wOF2"
        var woff2Data = new byte[]
        {
            (byte)'w', (byte)'O', (byte)'F', (byte)'2',
            0x00, 0x01, 0x00, 0x00,  // flavor
            0x00, 0x00, 0x00, 0x2C,  // length (44)
            0x00, 0x00,              // numTables
            0x00, 0x00, 0x00, 0x00,  // reserved
            0x00, 0x00, 0x00, 0x00,  // totalSfntSize
            0x00, 0x00, 0x00, 0x00,  // totalCompressedSize
            0x00, 0x00,              // majorVersion
            0x00, 0x00,              // minorVersion
            0x00, 0x00, 0x00, 0x00,  // metaOffset
            0x00, 0x00, 0x00, 0x00,  // metaLength
            0x00, 0x00, 0x00, 0x00,  // metaOrigLength
            0x00, 0x00, 0x00, 0x00,  // privOffset
            0x00, 0x00, 0x00, 0x00,  // privLength
        };

        // Act
        var act = () => WoffDecompressor.Decompress(woff2Data);

        // Assert
        var ex1 = Should.Throw<NotSupportedException>(act);
        ex1.Message.ShouldContain("WOFF2");
    }

    [Fact]
    public void WoffDecompressor_Woff2Data_ExceptionMessageIsDescriptive()
    {
        // Arrange
        var woff2Data = new byte[]
        {
            (byte)'w', (byte)'O', (byte)'F', (byte)'2',
            0x00, 0x00, 0x00, 0x00
        };

        // Act
        var act = () => WoffDecompressor.Decompress(woff2Data);

        // Assert — message should guide the user toward a workaround
        var ex = Should.Throw<NotSupportedException>(act);
        ex.Message.ShouldContain("Convert");
    }

    [Fact]
    public void Generate_WithWoff2Data_ThrowsNotSupportedException()
    {
        // Arrange — WOFF2 signature followed by enough data to pass initial checks
        var woff2Data = new byte[48];
        woff2Data[0] = (byte)'w';
        woff2Data[1] = (byte)'O';
        woff2Data[2] = (byte)'F';
        woff2Data[3] = (byte)'2';

        // Act
        var act = () => BmFont.Generate(woff2Data, new FontGeneratorOptions { Size = 32 });

        // Assert — BmFont.Generate detects WOFF2 and calls WoffDecompressor.Decompress,
        // which should throw NotSupportedException
        var ex2 = Should.Throw<NotSupportedException>(act);
        ex2.Message.ShouldContain("WOFF2");
    }
}
