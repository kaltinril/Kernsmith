using KernSmith.Output;
using FluentAssertions;

namespace KernSmith.Tests;

/// <summary>
/// Tests for the API usability features: FromConfig, convenience properties,
/// GetPngData/GetTgaData/GetDdsData, ToBmfc, Builder.FromConfig, and AtlasPage helpers.
/// </summary>
public sealed class ApiUsabilityTests : IDisposable
{
    private static readonly string TestFontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] LoadTestFont() => File.ReadAllBytes(TestFontPath);

    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch { /* best effort cleanup */ }
        }
    }

    private string CreateTempBmfc(string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_ApiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);

        var bmfcPath = Path.Combine(dir, "test.bmfc");
        File.WriteAllText(bmfcPath, content);
        return bmfcPath;
    }

    /// <summary>Builds a minimal .bmfc config string pointing at the test font.</summary>
    private string BuildBmfcContent(int fontSize = 32, string? extraLines = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"fontFile={TestFontPath}");
        sb.AppendLine($"fontSize={fontSize}");
        sb.AppendLine("chars=32-126");
        if (extraLines != null)
            sb.AppendLine(extraLines);
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // FromConfig — file path overload
    // ------------------------------------------------------------------

    [Fact]
    public void FromConfig_BmfcPath_ProducesResultWithCharactersAndPages()
    {
        // Arrange
        var bmfcPath = CreateTempBmfc(BuildBmfcContent());

        // Act
        var result = BmFont.FromConfig(bmfcPath);

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "FromConfig should produce character entries for ASCII 32-126");
        result.Pages.Should().HaveCountGreaterThan(0,
            "FromConfig should produce at least one atlas page");
    }

    // ------------------------------------------------------------------
    // FromConfig — BmfcConfig object overload
    // ------------------------------------------------------------------

    [Fact]
    public void FromConfig_BmfcConfigObject_ProducesResultWithCharactersAndPages()
    {
        // Arrange
        var config = new BmfcConfig
        {
            FontFile = TestFontPath,
            Options = new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.FromRanges((32, 126))
            }
        };

        // Act
        var result = BmFont.FromConfig(config);

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "FromConfig with BmfcConfig object should produce character entries");
        result.Pages.Should().HaveCountGreaterThan(0,
            "FromConfig with BmfcConfig object should produce at least one atlas page");
    }

    // ------------------------------------------------------------------
    // FromConfig — system font name
    // ------------------------------------------------------------------

    [Fact(Skip = "Requires system font 'Arial' which is not available on Linux CI runners")]
    public void FromConfig_SystemFontName_ProducesResult()
    {
        // Arrange
        var config = new BmfcConfig
        {
            FontName = "Arial",
            Options = new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.FromRanges((32, 126))
            }
        };

        // Act
        var result = BmFont.FromConfig(config);

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "FromConfig with system font name should produce character entries");
        result.Pages.Should().HaveCountGreaterThan(0,
            "FromConfig with system font name should produce at least one atlas page");
    }

    // ------------------------------------------------------------------
    // Convenience properties — FntText
    // ------------------------------------------------------------------

    [Fact]
    public void FntText_ReturnsNonEmptyStringContainingInfoFace()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var fntText = result.FntText;

        // Assert
        fntText.Should().NotBeNullOrWhiteSpace("FntText should return non-empty text");
        fntText.Should().Contain("info face=",
            "FntText should contain the BMFont text format info line");
    }

    // ------------------------------------------------------------------
    // Convenience properties — FntXml
    // ------------------------------------------------------------------

    [Fact]
    public void FntXml_ReturnsNonEmptyStringContainingFontElement()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var fntXml = result.FntXml;

        // Assert
        fntXml.Should().NotBeNullOrWhiteSpace("FntXml should return non-empty XML");
        fntXml.Should().Contain("<font>",
            "FntXml should contain the <font> root element");
    }

    // ------------------------------------------------------------------
    // Convenience properties — FntBinary
    // ------------------------------------------------------------------

    [Fact]
    public void FntBinary_ReturnsNonEmptyArrayWithBmfHeader()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var fntBinary = result.FntBinary;

        // Assert
        fntBinary.Should().NotBeEmpty("FntBinary should return non-empty byte array");
        fntBinary[0].Should().Be(66, "first byte of BMF header should be 'B' (66)");
        fntBinary[1].Should().Be(77, "second byte of BMF header should be 'M' (77)");
        fntBinary[2].Should().Be(70, "third byte of BMF header should be 'F' (70)");
    }

    // ------------------------------------------------------------------
    // GetPngData() — all pages
    // ------------------------------------------------------------------

    [Fact]
    public void GetPngData_AllPages_ReturnsAtLeastOneEntryWithPngHeader()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var pngDataAll = result.GetPngData();

        // Assert
        pngDataAll.Should().HaveCountGreaterThan(0,
            "GetPngData() should return at least one page");

        foreach (var pngData in pngDataAll)
        {
            pngData.Should().HaveCountGreaterThan(4,
                "each PNG entry should be non-trivially sized");
            pngData[0].Should().Be(137, "PNG header byte 0 should be 0x89 (137)");
            pngData[1].Should().Be(80, "PNG header byte 1 should be 'P' (80)");
            pngData[2].Should().Be(78, "PNG header byte 2 should be 'N' (78)");
            pngData[3].Should().Be(71, "PNG header byte 3 should be 'G' (71)");
        }
    }

    // ------------------------------------------------------------------
    // GetPngData(0) — single page
    // ------------------------------------------------------------------

    [Fact]
    public void GetPngData_FirstPage_ReturnsPngBytes()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var pngData = result.GetPngData(0);

        // Assert
        pngData.Should().HaveCountGreaterThan(4, "PNG data should be non-trivially sized");
        pngData[0].Should().Be(137, "PNG header byte 0 should be 0x89 (137)");
        pngData[1].Should().Be(80, "PNG header byte 1 should be 'P' (80)");
        pngData[2].Should().Be(78, "PNG header byte 2 should be 'N' (78)");
        pngData[3].Should().Be(71, "PNG header byte 3 should be 'G' (71)");
    }

    // ------------------------------------------------------------------
    // GetPngData — negative index
    // ------------------------------------------------------------------

    [Fact]
    public void GetPngData_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var act = () => result.GetPngData(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ------------------------------------------------------------------
    // GetPngData — out of range index
    // ------------------------------------------------------------------

    [Fact]
    public void GetPngData_IndexBeyondPageCount_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var act = () => result.GetPngData(999);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ------------------------------------------------------------------
    // GetTgaData — single page
    // ------------------------------------------------------------------

    [Fact]
    public void GetTgaData_FirstPage_ReturnsNonEmptyBytes()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var tgaData = result.GetTgaData(0);

        // Assert
        tgaData.Should().NotBeEmpty("GetTgaData(0) should return non-empty TGA bytes");
        tgaData.Length.Should().BeGreaterThan(18,
            "TGA data should be larger than just the 18-byte header");
    }

    // ------------------------------------------------------------------
    // GetDdsData — single page
    // ------------------------------------------------------------------

    [Fact]
    public void GetDdsData_FirstPage_ReturnsNonEmptyBytesWithDdsHeader()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var ddsData = result.GetDdsData(0);

        // Assert
        ddsData.Should().NotBeEmpty("GetDdsData(0) should return non-empty DDS bytes");
        // "DDS " magic: 0x44, 0x44, 0x53, 0x20
        ddsData[0].Should().Be(0x44, "DDS header byte 0 should be 'D'");
        ddsData[1].Should().Be(0x44, "DDS header byte 1 should be 'D'");
        ddsData[2].Should().Be(0x53, "DDS header byte 2 should be 'S'");
        ddsData[3].Should().Be(0x20, "DDS header byte 3 should be ' '");
    }

    // ------------------------------------------------------------------
    // ToBmfc — round-trip options
    // ------------------------------------------------------------------

    [Fact]
    public void ToBmfc_WithSpecificOptions_ContainsExpectedValues()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126)),
            Outline = 2,
            Bold = true
        });

        // Act
        var bmfc = result.ToBmfc();

        // Assert
        bmfc.Should().Contain("fontSize=32",
            "ToBmfc should include fontSize=32");
        bmfc.Should().Contain("outlineThickness=2",
            "ToBmfc should include outlineThickness=2");
        bmfc.Should().Contain("isBold=1",
            "ToBmfc should include isBold=1");
    }

    // ------------------------------------------------------------------
    // Builder.FromConfig — file path
    // ------------------------------------------------------------------

    [Fact]
    public void Builder_FromConfig_BmfcPath_GeneratesSuccessfully()
    {
        // Arrange
        var bmfcPath = CreateTempBmfc(BuildBmfcContent());

        // Act
        var result = BmFont.Builder().FromConfig(bmfcPath).Build();

        // Assert
        result.Model.Characters.Should().HaveCountGreaterThan(0,
            "Builder.FromConfig should produce character entries");
        result.Pages.Should().HaveCountGreaterThan(0,
            "Builder.FromConfig should produce at least one atlas page");
    }

    // ------------------------------------------------------------------
    // Builder.FromConfig — size override
    // ------------------------------------------------------------------

    [Fact]
    public void Builder_FromConfig_WithSizeOverride_ReflectsOverriddenSize()
    {
        // Arrange — .bmfc has fontSize=32, but we override to 48
        var bmfcPath = CreateTempBmfc(BuildBmfcContent(fontSize: 32));

        // Act
        var result = BmFont.Builder()
            .FromConfig(bmfcPath)
            .WithSize(48)
            .Build();

        // Assert — the info block should report size 48, not 32
        result.Model.Info.Size.Should().Be(48,
            "WithSize(48) after FromConfig should override the config's fontSize=32");
    }

    // ------------------------------------------------------------------
    // AtlasPage.ToTga
    // ------------------------------------------------------------------

    [Fact]
    public void AtlasPage_ToTga_ReturnsNonEmptyBytes()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var tgaBytes = result.Pages[0].ToTga();

        // Assert
        tgaBytes.Should().NotBeEmpty("AtlasPage.ToTga() should return non-empty TGA bytes");
        tgaBytes.Length.Should().BeGreaterThan(18,
            "TGA data should be larger than just the 18-byte header");
    }

    // ------------------------------------------------------------------
    // AtlasPage.ToDds
    // ------------------------------------------------------------------

    [Fact]
    public void AtlasPage_ToDds_ReturnsNonEmptyBytesWithDdsHeader()
    {
        // Arrange
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromRanges((32, 126))
        });

        // Act
        var ddsBytes = result.Pages[0].ToDds();

        // Assert
        ddsBytes.Should().NotBeEmpty("AtlasPage.ToDds() should return non-empty DDS bytes");
        ddsBytes[0].Should().Be(0x44, "DDS header byte 0 should be 'D'");
        ddsBytes[1].Should().Be(0x44, "DDS header byte 1 should be 'D'");
        ddsBytes[2].Should().Be(0x53, "DDS header byte 2 should be 'S'");
        ddsBytes[3].Should().Be(0x20, "DDS header byte 3 should be ' '");
    }
}
