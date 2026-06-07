using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class BmfcConfigReaderWriterTests
{
    [Fact]
    public void Parse_FontSize_SetsSize()
    {
        // Act
        var config = BmfcConfigReader.Parse("fontSize=48\n");

        // Assert
        config.Options.Size.ShouldBe(48f);
        config.Options.MatchCharHeight.ShouldBeFalse();
    }

    [Fact]
    public void Parse_NegativeFontSize_EnablesMatchCharHeight()
    {
        // Act
        var config = BmfcConfigReader.Parse("fontSize=-32\n");

        // Assert
        config.Options.Size.ShouldBe(32f);
        config.Options.MatchCharHeight.ShouldBeTrue();
    }

    [Fact]
    public void Parse_BoldAndItalicFlags_AreParsed()
    {
        // Act
        var config = BmfcConfigReader.Parse("isBold=1\nisItalic=1\n");

        // Assert
        config.Options.Bold.ShouldBeTrue();
        config.Options.Italic.ShouldBeTrue();
    }

    [Fact]
    public void Parse_UseSmoothingZero_DisablesAntiAlias()
    {
        // Act
        var config = BmfcConfigReader.Parse("useSmoothing=0\n");

        // Assert
        config.Options.AntiAlias.ShouldBe(AntiAliasMode.None);
    }

    [Fact]
    public void Parse_DontIncludeKerningPairs_DisablesKerning()
    {
        // Act
        var config = BmfcConfigReader.Parse("dontIncludeKerningPairs=1\n");

        // Assert
        config.Options.Kerning.ShouldBeFalse();
    }

    [Fact]
    public void Parse_Padding_PopulatesAllSides()
    {
        // Act
        var config = BmfcConfigReader.Parse(
            "paddingUp=1\npaddingRight=2\npaddingDown=3\npaddingLeft=4\n");

        // Assert
        config.Options.Padding.ShouldBe(new Padding(1, 2, 3, 4));
    }

    [Fact]
    public void Parse_Spacing_PopulatesHorizontalAndVertical()
    {
        // Act
        var config = BmfcConfigReader.Parse("spacingHoriz=5\nspacingVert=7\n");

        // Assert
        config.Options.Spacing.ShouldBe(new Spacing(5, 7));
    }

    [Fact]
    public void Parse_TextureFormat_MapsToEnum()
    {
        // Act
        var config = BmfcConfigReader.Parse("textureFormat=dds\n");

        // Assert
        config.Options.TextureFormat.ShouldBe(TextureFormat.Dds);
    }

    [Fact]
    public void Parse_FontDescFormat_MapsToOutputFormat()
    {
        // Act
        var config = BmfcConfigReader.Parse("fontDescFormat=2\n");

        // Assert
        config.OutputFormat.ShouldBe(OutputFormat.Binary);
    }

    [Fact]
    public void Parse_Chars_BuildsCharacterSet()
    {
        // Act
        var config = BmfcConfigReader.Parse("chars=65-67,90\n");

        // Assert
        config.Options.Characters.GetCodepoints().ShouldBe(new[] { 65, 66, 67, 90 });
    }

    [Fact]
    public void Parse_OutlineColor_ParsesHex()
    {
        // Act
        var config = BmfcConfigReader.Parse("outlineThickness=2\noutlineColor=FF8000\n");

        // Assert
        config.Options.Outline.ShouldBe(2);
        config.Options.OutlineR.ShouldBe((byte)0xFF);
        config.Options.OutlineG.ShouldBe((byte)0x80);
        config.Options.OutlineB.ShouldBe((byte)0x00);
    }

    [Fact]
    public void Parse_GradientColors_ParseTopAndBottom()
    {
        // Act
        var config = BmfcConfigReader.Parse("gradientTop=FF0000\ngradientBottom=0000FF\n");

        // Assert
        config.Options.GradientStartR.ShouldBe((byte)0xFF);
        config.Options.GradientEndB.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void Parse_UnknownKey_IsIgnored()
    {
        // Act
        var config = BmfcConfigReader.Parse("someFutureKey=hello\nfontSize=20\n");

        // Assert — unknown keys are skipped for forward compatibility
        config.Options.Size.ShouldBe(20f);
    }

    [Fact]
    public void Parse_CommentsAndBlankLines_AreSkipped()
    {
        // Act
        var config = BmfcConfigReader.Parse("# comment\n\nfontSize=16\n");

        // Assert
        config.Options.Size.ShouldBe(16f);
    }

    [Fact]
    public void Parse_RasterizerBackend_IsCaseInsensitive()
    {
        // Act
        var config = BmfcConfigReader.Parse("rasterizer=stbtruetype\n");

        // Assert
        config.Options.Backend.ShouldBe(RasterizerBackend.StbTrueType);
    }

    [Fact]
    public void Read_MissingFile_ThrowsFileNotFound()
    {
        // Act & Assert
        Should.Throw<FileNotFoundException>(
            () => BmfcConfigReader.Read(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.bmfc")));
    }

    [Fact]
    public void Write_ProducesAngelCodeHeader()
    {
        // Arrange
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions());

        // Act
        var text = BmfcConfigWriter.Write(config);

        // Assert
        text.ShouldContain("# AngelCode Bitmap Font Generator");
        text.ShouldContain("fileVersion=1");
    }

    [Fact]
    public void WriteThenParse_RoundTripsCoreOptions()
    {
        // Arrange
        var original = new FontGeneratorOptions
        {
            Size = 64,
            Bold = true,
            Italic = true,
            Kerning = false,
            HeightPercent = 120,
            MaxTextureWidth = 2048,
            MaxTextureHeight = 1024,
            TextureFormat = TextureFormat.Tga,
            Outline = 3,
            Padding = new Padding(1, 2, 3, 4),
            Spacing = new Spacing(2, 3),
            Characters = CharacterSet.FromRanges((65, 90)),
        };
        var config = BmfcConfig.FromOptions(original, outputFormat: OutputFormat.Xml);

        // Act
        var text = BmfcConfigWriter.Write(config);
        var roundTripped = BmfcConfigReader.Parse(text);
        var result = roundTripped.Options;

        // Assert
        result.Size.ShouldBe(64f);
        result.Bold.ShouldBeTrue();
        result.Italic.ShouldBeTrue();
        result.Kerning.ShouldBeFalse();
        result.HeightPercent.ShouldBe(120);
        result.MaxTextureWidth.ShouldBe(2048);
        result.MaxTextureHeight.ShouldBe(1024);
        result.TextureFormat.ShouldBe(TextureFormat.Tga);
        result.Outline.ShouldBe(3);
        result.Padding.ShouldBe(new Padding(1, 2, 3, 4));
        result.Spacing.ShouldBe(new Spacing(2, 3));
        result.Characters.GetCodepoints().ShouldBe(Enumerable.Range(65, 26));
        roundTripped.OutputFormat.ShouldBe(OutputFormat.Xml);
    }

    [Fact]
    public void WriteThenParse_RoundTripsExtensionOptions()
    {
        // Arrange
        var original = new FontGeneratorOptions
        {
            Sdf = true,
            SuperSampleLevel = 4,
            PackingAlgorithm = PackingAlgorithm.Skyline,
            ColorFont = true,
            FaceIndex = 2,
            Dpi = 96,
            PowerOfTwo = false,
            AutofitTexture = true,
            Backend = RasterizerBackend.StbTrueType,
            ShadowOffsetX = 2,
            ShadowOffsetY = 3,
            ShadowBlur = 1,
            ShadowR = 0x10,
            ShadowG = 0x20,
            ShadowB = 0x30,
        };
        var config = BmfcConfig.FromOptions(original);

        // Act
        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        // Assert
        result.Sdf.ShouldBeTrue();
        result.SuperSampleLevel.ShouldBe(4);
        result.PackingAlgorithm.ShouldBe(PackingAlgorithm.Skyline);
        result.ColorFont.ShouldBeTrue();
        result.FaceIndex.ShouldBe(2);
        result.Dpi.ShouldBe(96);
        result.PowerOfTwo.ShouldBeFalse();
        result.AutofitTexture.ShouldBeTrue();
        result.Backend.ShouldBe(RasterizerBackend.StbTrueType);
        result.ShadowOffsetX.ShouldBe(2);
        result.ShadowOffsetY.ShouldBe(3);
        result.ShadowBlur.ShouldBe(1);
        result.ShadowR.ShouldBe((byte)0x10);
        result.ShadowG.ShouldBe((byte)0x20);
        result.ShadowB.ShouldBe((byte)0x30);
    }

    [Fact]
    public void WriteThenParse_RoundTripsAdvanceAdjustXandY()
    {
        // Arrange
        var original = new FontGeneratorOptions
        {
            AdvanceAdjustX = 1.5f,
            AdvanceAdjustY = -2.25f,
        };
        var config = BmfcConfig.FromOptions(original);

        // Act
        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        // Assert
        result.AdvanceAdjustX.ShouldBe(1.5f);
        result.AdvanceAdjustY.ShouldBe(-2.25f);
    }

    [Fact]
    public void WriteThenParse_DefaultAdvanceAdjustY_EmitsNothing()
    {
        // Default (0) must not be written, keeping output identical to before.
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions());

        var text = BmfcConfigWriter.Write(config);

        text.ShouldNotContain("advanceAdjustY");
    }

    [Fact]
    public void WriteToFile_CreatesReadableFile()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), $"ks-bmfc-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "config.bmfc");
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions { Size = 24 });

        try
        {
            // Act
            BmfcConfigWriter.WriteToFile(config, path);
            var read = BmfcConfigReader.Read(path);

            // Assert
            File.Exists(path).ShouldBeTrue();
            read.Options.Size.ShouldBe(24f);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Read_ResolvesRelativeFontFileAgainstConfigDirectory()
    {
        // Arrange
        var dir = Path.Combine(Path.GetTempPath(), $"ks-bmfc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.bmfc");
        File.WriteAllText(path, "fontFile=sub/font.ttf\n");

        try
        {
            // Act
            var config = BmfcConfigReader.Read(path);

            // Assert
            Path.IsPathRooted(config.FontFile).ShouldBeTrue();
            config.FontFile!.ShouldBe(Path.GetFullPath(Path.Combine(dir, "sub", "font.ttf")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
