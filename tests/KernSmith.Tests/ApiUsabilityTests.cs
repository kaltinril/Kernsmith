using KernSmith.Output;
using Shouldly;

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
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "FromConfig should produce character entries for ASCII 32-126");
        result.Pages.Count.ShouldBeGreaterThan(0,
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "FromConfig with BmfcConfig object should produce character entries");
        result.Pages.Count.ShouldBeGreaterThan(0,
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "FromConfig with system font name should produce character entries");
        result.Pages.Count.ShouldBeGreaterThan(0,
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
        fntText.ShouldNotBeNullOrWhiteSpace();
        fntText.ShouldContain("info face=");
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
        fntXml.ShouldNotBeNullOrWhiteSpace();
        fntXml.ShouldContain("<font>");
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
        fntBinary.ShouldNotBeEmpty();
        fntBinary[0].ShouldBe((byte)66);
        fntBinary[1].ShouldBe((byte)77);
        fntBinary[2].ShouldBe((byte)70);
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
        pngDataAll.Length.ShouldBeGreaterThan(0,
            "GetPngData() should return at least one page");

        foreach (var pngData in pngDataAll)
        {
            pngData.Length.ShouldBeGreaterThan(4,
                "each PNG entry should be non-trivially sized");
            pngData[0].ShouldBe((byte)137);
            pngData[1].ShouldBe((byte)80);
            pngData[2].ShouldBe((byte)78);
            pngData[3].ShouldBe((byte)71);
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
        pngData.Length.ShouldBeGreaterThan(4);
        pngData[0].ShouldBe((byte)137);
        pngData[1].ShouldBe((byte)80);
        pngData[2].ShouldBe((byte)78);
        pngData[3].ShouldBe((byte)71);
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
        Should.Throw<ArgumentOutOfRangeException>(act);
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
        Should.Throw<ArgumentOutOfRangeException>(act);
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
        tgaData.ShouldNotBeEmpty();
        tgaData.Length.ShouldBeGreaterThan(18,
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
        ddsData.ShouldNotBeEmpty();
        // "DDS " magic: 0x44, 0x44, 0x53, 0x20
        ddsData[0].ShouldBe((byte)0x44);
        ddsData[1].ShouldBe((byte)0x44);
        ddsData[2].ShouldBe((byte)0x53);
        ddsData[3].ShouldBe((byte)0x20);
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
        bmfc.ShouldContain("fontSize=32");
        bmfc.ShouldContain("outlineThickness=2");
        bmfc.ShouldContain("isBold=1");
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
        result.Model.Characters.Count.ShouldBeGreaterThan(0,
            "Builder.FromConfig should produce character entries");
        result.Pages.Count.ShouldBeGreaterThan(0,
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
        result.Model.Info.Size.ShouldBe(48,
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
        tgaBytes.ShouldNotBeEmpty();
        tgaBytes.Length.ShouldBeGreaterThan(18,
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
        ddsBytes.ShouldNotBeEmpty();
        ddsBytes[0].ShouldBe((byte)0x44);
        ddsBytes[1].ShouldBe((byte)0x44);
        ddsBytes[2].ShouldBe((byte)0x53);
        ddsBytes[3].ShouldBe((byte)0x20);
    }
}
