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
    public void Parse_PerChannelKeys_PopulateChannelConfig()
    {
        // Act -- glyph in alpha, RGB forced to one (BMFont: 0=glyph, 4=one)
        var config = BmfcConfigReader.Parse(
            "alphaChnl=0\nredChnl=4\ngreenChnl=4\nblueChnl=4\n");

        // Assert
        config.Options.Channels.ShouldNotBeNull();
        config.Options.Channels!.Alpha.ShouldBe(ChannelContent.Glyph);
        config.Options.Channels!.Red.ShouldBe(ChannelContent.One);
        config.Options.Channels!.Green.ShouldBe(ChannelContent.One);
        config.Options.Channels!.Blue.ShouldBe(ChannelContent.One);
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
    public void WriteThenParse_RoundTripsChannelConfig()
    {
        // Arrange -- the white-on-alpha layout BMFont writes: glyph in alpha, RGB forced to one.
        var original = new FontGeneratorOptions
        {
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Glyph,
                Red: ChannelContent.One,
                Green: ChannelContent.One,
                Blue: ChannelContent.One),
        };
        var config = BmfcConfig.FromOptions(original);

        // Act
        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        // Assert
        result.Channels.ShouldNotBeNull();
        result.Channels!.Alpha.ShouldBe(ChannelContent.Glyph);
        result.Channels!.Red.ShouldBe(ChannelContent.One);
        result.Channels!.Green.ShouldBe(ChannelContent.One);
        result.Channels!.Blue.ShouldBe(ChannelContent.One);
    }

    [Fact]
    public void WriteThenParse_RoundTripsChannelInversionFlags()
    {
        // Arrange
        var original = new FontGeneratorOptions
        {
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Outline,
                Red: ChannelContent.Zero,
                Green: ChannelContent.GlyphAndOutline,
                Blue: ChannelContent.One,
                InvertAlpha: true,
                InvertRed: false,
                InvertGreen: true,
                InvertBlue: false),
        };
        var config = BmfcConfig.FromOptions(original);

        // Act
        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        // Assert
        result.Channels.ShouldNotBeNull();
        result.Channels!.Alpha.ShouldBe(ChannelContent.Outline);
        result.Channels!.Red.ShouldBe(ChannelContent.Zero);
        result.Channels!.Green.ShouldBe(ChannelContent.GlyphAndOutline);
        result.Channels!.Blue.ShouldBe(ChannelContent.One);
        result.Channels!.InvertAlpha.ShouldBeTrue();
        result.Channels!.InvertRed.ShouldBeFalse();
        result.Channels!.InvertGreen.ShouldBeTrue();
        result.Channels!.InvertBlue.ShouldBeFalse();
    }

    [Fact]
    public void WriteThenParse_RoundTripsShadowChannelContent()
    {
        // ChannelContent.Shadow (5) is a KernSmith extension beyond BMFont's 0-4 range.
        var original = new FontGeneratorOptions
        {
            Channels = new ChannelConfig(Blue: ChannelContent.Shadow),
        };
        var config = BmfcConfig.FromOptions(original);

        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        result.Channels.ShouldNotBeNull();
        result.Channels!.Blue.ShouldBe(ChannelContent.Shadow);
    }

    [Fact]
    public void Write_DefaultChannelConfig_EmitsNoChannelKeys()
    {
        // An unset or all-default ChannelConfig must not emit the keys: doing so would turn
        // Channels from null into a non-null default on round-trip, which downstream code
        // treats as "channel routing requested".
        var unset = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));
        var explicitDefault = BmfcConfigWriter.Write(
            BmfcConfig.FromOptions(new FontGeneratorOptions { Channels = new ChannelConfig() }));

        foreach (var text in new[] { unset, explicitDefault })
        {
            text.ShouldNotContain("alphaChnl");
            text.ShouldNotContain("redChnl");
            text.ShouldNotContain("greenChnl");
            text.ShouldNotContain("blueChnl");
            text.ShouldNotContain("invA=");
            text.ShouldNotContain("invR=");
            text.ShouldNotContain("invG=");
            text.ShouldNotContain("invB=");
        }
    }

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void WriteThenParse_RoundTripsAllAntiAliasModes(AntiAliasMode mode)
    {
        // Regression: the writer used to emit the AA mode into `aa`, which BMFont (and our own
        // reader) define as the supersampling factor. AntiAliasMode.Light wrote aa=2 and came
        // back as Grayscale with SuperSampleLevel=2 -- a silent 2x supersample on reload.
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions { AntiAlias = mode });

        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        result.AntiAlias.ShouldBe(mode);
        result.SuperSampleLevel.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void WriteThenParse_AaKeyCarriesSuperSampleLevel(int level)
    {
        // `aa` is BMFont's supersampling factor, so it must carry SuperSampleLevel -- both for
        // our own round trip and so BMFont.exe reads a KernSmith config correctly.
        var config = BmfcConfig.FromOptions(
            new FontGeneratorOptions { SuperSampleLevel = level });

        var text = BmfcConfigWriter.Write(config);

        text.ShouldContain($"aa={level}");
        BmfcConfigReader.Parse(text).Options.SuperSampleLevel.ShouldBe(level);
    }

    [Fact]
    public void WriteThenParse_RoundTripsShadowOpacity()
    {
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions { ShadowOpacity = 0.5f });

        BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options
            .ShadowOpacity.ShouldBe(0.5f);
    }

    [Fact]
    public void WriteThenParse_RoundTripsSdfScale()
    {
        var config = BmfcConfig.FromOptions(
            new FontGeneratorOptions { Sdf = true, SdfScale = 3 });

        BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options
            .SdfScale.ShouldBe(3);
    }

    [Fact]
    public void WriteThenParse_RoundTripsHardShadow()
    {
        // HardShadow also feeds the variantShadow round trip: BmfcConfigReader rebuilds the
        // shadow AtlasVariant from options.HardShadow, so losing it degrades the variant too.
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions { HardShadow = true });

        BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options
            .HardShadow.ShouldBeTrue();
    }

    [Fact]
    public void WriteThenParse_RoundTripsVariationAxes()
    {
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions
        {
            VariationAxes = new Dictionary<string, float> { ["wght"] = 700f, ["wdth"] = 87.5f },
        });

        var result = BmfcConfigReader.Parse(BmfcConfigWriter.Write(config)).Options;

        result.VariationAxes.ShouldNotBeNull();
        result.VariationAxes!["wght"].ShouldBe(700f);
        result.VariationAxes!["wdth"].ShouldBe(87.5f);
    }

    [Fact]
    public void Write_DefaultsForNewlyPersistedOptions_EmitNothing()
    {
        // Keeps default .bmfc output byte-identical as these keys were added.
        var text = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));

        text.ShouldNotContain("shadowOpacity");
        text.ShouldNotContain("sdfScale");
        text.ShouldNotContain("hardShadow");
        text.ShouldNotContain("variationAxes");
        text.ShouldNotContain("antiAlias=");
    }

    [Theory]
    // The white-on-alpha layout every BMFont-authored config in tests/bmfont-compare uses.
    [InlineData(0, 4, 4, 4)]
    // Outline in alpha, glyph in RGB (Font48Bauhaus_93_o4_Bold.bmfc).
    [InlineData(1, 0, 0, 0)]
    public void ParseThenWrite_PreservesChannelKeysFromRealConfig(
        int alpha, int red, int green, int blue)
    {
        // The reported bug is load->save, so drive the round trip from .bmfc text inward --
        // the other tests all start from options and would miss a BmfcConfig plumbing break.
        var source =
            $"alphaChnl={alpha}\nredChnl={red}\ngreenChnl={green}\nblueChnl={blue}\n";

        var written = BmfcConfigWriter.Write(BmfcConfigReader.Parse(source));

        written.ShouldContain($"alphaChnl={alpha}");
        written.ShouldContain($"redChnl={red}");
        written.ShouldContain($"greenChnl={green}");
        written.ShouldContain($"blueChnl={blue}");
    }

    [Fact]
    public void ParseThenWrite_PartialChannelKeys_NormalizesToFullKeySet()
    {
        // A config carrying only an inversion flag is still non-default, so the writer must
        // emit the whole set rather than the single key it came from.
        var written = BmfcConfigWriter.Write(BmfcConfigReader.Parse("invA=1\n"));

        written.ShouldContain("alphaChnl=0");
        written.ShouldContain("redChnl=0");
        written.ShouldContain("greenChnl=0");
        written.ShouldContain("blueChnl=0");
        written.ShouldContain("invA=1");
        written.ShouldContain("invR=0");
        written.ShouldContain("invG=0");
        written.ShouldContain("invB=0");
    }

    [Fact]
    public void Write_IsFixedPointAfterOneRoundTrip()
    {
        // Guards key ordering, duplication, and the partial-key normalization path above.
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions
        {
            Channels = new ChannelConfig(
                Alpha: ChannelContent.Outline,
                Red: ChannelContent.One,
                Green: ChannelContent.Zero,
                Blue: ChannelContent.GlyphAndOutline,
                InvertGreen: true),
        });

        var once = BmfcConfigWriter.Write(config);
        var twice = BmfcConfigWriter.Write(BmfcConfigReader.Parse(once));

        twice.ShouldBe(once);
    }

    [Fact]
    public void ParseThenWrite_ExplicitlyDefaultChannelKeys_AreIntentionallyDropped()
    {
        // Pins a deliberate asymmetry: a config whose channel keys all hold their default
        // values reads as a non-null-but-default ChannelConfig, which the writer suppresses.
        // Behaviourally free -- both BmFont.ShouldApplyChannelConfig and BmFontModelBuilder
        // gate on `is { IsDefault: false }`, so null and all-default render identically.
        // Documented here so a future reader change cannot flip it silently.
        var source = "alphaChnl=0\nredChnl=0\ngreenChnl=0\nblueChnl=0\n"
            + "invA=0\ninvR=0\ninvG=0\ninvB=0\n";

        var parsed = BmfcConfigReader.Parse(source).Options;
        var reparsed = BmfcConfigReader.Parse(
            BmfcConfigWriter.Write(BmfcConfig.FromOptions(parsed))).Options;

        parsed.Channels.ShouldNotBeNull();
        parsed.Channels!.IsDefault.ShouldBeTrue();
        reparsed.Channels.ShouldBeNull();
    }

    [Fact]
    public void Write_DefaultChannelConfig_LeavesOutputUnchanged()
    {
        // Guards the byte-identical-by-default promise: an all-default ChannelConfig must
        // produce exactly the same .bmfc text as no ChannelConfig at all.
        var unset = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));
        var explicitDefault = BmfcConfigWriter.Write(
            BmfcConfig.FromOptions(new FontGeneratorOptions { Channels = new ChannelConfig() }));

        explicitDefault.ShouldBe(unset);
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
