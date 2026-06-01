using KernSmith.Output;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Tests for Hiero .hiero config read/write support: HieroConfigReader, HieroConfigWriter,
/// ConfigFormatFactory auto-detection, and BmFontResult.ToHiero().
/// </summary>
[Collection("RasterizerFactory")]
public sealed class HieroConfigTests : IDisposable
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

    private string CreateTempFile(string fileName, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);

        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private const string EffectPkg = "com.badlogic.gdx.tools.hiero.unicodefont.effects.";

    // ------------------------------------------------------------------
    // Parse: font properties
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_FontProperties_MapsToOptions()
    {
        var content =
            "font.name=Arial\n" +
            "font.size=48\n" +
            "font.bold=true\n" +
            "font.italic=true\n" +
            "font.mono=true\n" +
            "glyph.text=AB\n";

        var config = HieroConfigReader.Parse(content);

        config.FontName.ShouldBe("Arial");
        config.Options.Size.ShouldBe(48f);
        config.Options.Bold.ShouldBeTrue();
        config.Options.Italic.ShouldBeTrue();
        config.Options.AntiAlias.ShouldBe(AntiAliasMode.None);
    }

    [Fact]
    public void Parse_Padding_MapsToPadding()
    {
        var content =
            "pad.top=1\n" +
            "pad.right=2\n" +
            "pad.bottom=3\n" +
            "pad.left=4\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Padding.Up.ShouldBe(1);
        config.Options.Padding.Right.ShouldBe(2);
        config.Options.Padding.Down.ShouldBe(3);
        config.Options.Padding.Left.ShouldBe(4);
    }

    [Fact]
    public void Parse_TexturePageSize_MapsToMaxTexture()
    {
        var content =
            "glyph.page.width=256\n" +
            "glyph.page.height=128\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.MaxTextureWidth.ShouldBe(256);
        config.Options.MaxTextureHeight.ShouldBe(128);
    }

    [Fact]
    public void Parse_Font2File_UsedWhenFont2UseTrue()
    {
        var content =
            "font2.file=MyFont.ttf\n" +
            "font2.use=true\n";

        var config = HieroConfigReader.Parse(content);

        config.FontFile.ShouldBe("MyFont.ttf");
    }

    [Fact]
    public void Parse_Font2File_IgnoredWhenFont2UseFalse()
    {
        var content =
            "font2.file=MyFont.ttf\n" +
            "font2.use=false\n";

        var config = HieroConfigReader.Parse(content);

        config.FontFile.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // pad.advance dropped with warning
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_PadAdvance_DroppedNotMapped()
    {
        // DELIBERATE DIVERGENCE FROM REF-10: REF-10 documents a pad.advance -> Spacing
        // mapping, but KernSmith intentionally DROPS pad.advance.x/y (Hiero's per-glyph
        // advance adjustment has no faithful KernSmith equivalent; the reader emits a
        // Debug.WriteLine warning and moves on). Spacing must therefore stay at its
        // default (1,1) even when pad.advance keys are present.
        var content =
            "pad.advance.x=-2\n" +
            "pad.advance.y=-2\n";

        // Should not throw and should leave Spacing at default (1,1).
        var config = HieroConfigReader.Parse(content);

        config.Options.Spacing.Horizontal.ShouldBe(1, "pad.advance.x is intentionally dropped, not mapped to Spacing.Horizontal");
        config.Options.Spacing.Vertical.ShouldBe(1, "pad.advance.y is intentionally dropped, not mapped to Spacing.Vertical");
        // Confirm the negative pad.advance values did NOT bleed into Spacing despite REF-10's mapping.
        config.Options.Spacing.ShouldBe(new Spacing(1, 1));
    }

    // ------------------------------------------------------------------
    // Character encoding via glyph.text
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_GlyphText_Ascii_BuildsCharacterSet()
    {
        var content = "glyph.text=ABC\n";

        var config = HieroConfigReader.Parse(content);

        var codepoints = config.Options.Characters.GetCodepoints().ToList();
        codepoints.ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
    }

    [Fact]
    public void Parse_GlyphText_Unicode_PreservesCharacters()
    {
        var content = "glyph.text=Aé€\n"; // 'A', 'é', '€'

        var config = HieroConfigReader.Parse(content);

        var codepoints = config.Options.Characters.GetCodepoints().ToList();
        codepoints.ShouldContain((int)'A');
        codepoints.ShouldContain(0x00e9);
        codepoints.ShouldContain(0x20ac);
    }

    [Fact]
    public void Parse_GlyphText_EscapedNewline_Unescaped()
    {
        var content = "glyph.text=A\\nB\n"; // literal backslash-n between A and B

        var config = HieroConfigReader.Parse(content);

        var codepoints = config.Options.Characters.GetCodepoints().ToList();
        codepoints.ShouldContain((int)'A');
        codepoints.ShouldContain((int)'B');
        codepoints.ShouldContain((int)'\n');
    }

    // ------------------------------------------------------------------
    // Effects: Outline
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_OutlineEffect_MapsWidthAndColor()
    {
        var content =
            $"effect.class={EffectPkg}OutlineEffect\n" +
            "effect.Color=ff0000\n" +
            "effect.Width=3.0\n" +
            "effect.Join=0\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(3);
        config.Options.OutlineR.ShouldBe((byte)0xff);
        config.Options.OutlineG.ShouldBe((byte)0x00);
        config.Options.OutlineB.ShouldBe((byte)0x00);
    }

    [Fact]
    public void Parse_OutlineEffect_FloatWidth_RoundedToInt()
    {
        // 2.6 rounds to 3, not truncated to 2.
        var content =
            $"effect.class={EffectPkg}OutlineEffect\n" +
            "effect.Color=000000\n" +
            "effect.Width=2.6\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(3);
    }

    // ------------------------------------------------------------------
    // Effects: Gradient
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_GradientEffect_MapsTopAndBottomColors()
    {
        var content =
            $"effect.class={EffectPkg}GradientEffect\n" +
            "effect.Top color=00ffff\n" +
            "effect.Bottom color=0000ff\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.GradientStartR.ShouldBe((byte)0x00);
        config.Options.GradientStartG.ShouldBe((byte)0xff);
        config.Options.GradientStartB.ShouldBe((byte)0xff);
        config.Options.GradientEndR.ShouldBe((byte)0x00);
        config.Options.GradientEndG.ShouldBe((byte)0x00);
        config.Options.GradientEndB.ShouldBe((byte)0xff);
    }

    // ------------------------------------------------------------------
    // Effects: Shadow
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_ShadowEffect_MapsOffsetColorOpacityBlur()
    {
        var content =
            $"effect.class={EffectPkg}ShadowEffect\n" +
            "effect.Color=112233\n" +
            "effect.Opacity=0.5\n" +
            "effect.X distance=3.0\n" +
            "effect.Y distance=4.0\n" +
            "effect.Blur kernel size=2\n" +
            "effect.Blur passes=1\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.ShadowOffsetX.ShouldBe(3);
        config.Options.ShadowOffsetY.ShouldBe(4);
        config.Options.ShadowR.ShouldBe((byte)0x11);
        config.Options.ShadowG.ShouldBe((byte)0x22);
        config.Options.ShadowB.ShouldBe((byte)0x33);
        config.Options.ShadowOpacity.ShouldBe(0.5f);
        config.Options.ShadowBlur.ShouldBe(2);
    }

    // ------------------------------------------------------------------
    // Effects: DistanceField <-> Sdf
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_DistanceFieldEffect_SetsSdf()
    {
        var content =
            $"effect.class={EffectPkg}DistanceFieldEffect\n" +
            "effect.Color=ffffff\n" +
            "effect.Scale=1\n" +
            "effect.Spread=1.0\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Sdf.ShouldBeTrue();
    }

    [Fact]
    public void Write_Sdf_EmitsDistanceFieldEffect()
    {
        var config = new BmfcConfig { Options = new FontGeneratorOptions { Sdf = true } };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("DistanceFieldEffect");
    }

    [Fact]
    public void Parse_UnknownEffect_SkippedGracefully()
    {
        var content =
            $"effect.class={EffectPkg}OutlineWobbleEffect\n" +
            "effect.Width=2.0\n" +
            "effect.Detail=1\n";

        // Should not throw; wobble has no equivalent so Outline stays 0.
        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(0);
    }

    // ------------------------------------------------------------------
    // Writer
    // ------------------------------------------------------------------

    [Fact]
    public void Write_FontProperties_Serialized()
    {
        var config = new BmfcConfig
        {
            FontName = "Arial",
            Options = new FontGeneratorOptions { Size = 24, Bold = true, Italic = true }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("font.name=Arial");
        hiero.ShouldContain("font.size=24");
        hiero.ShouldContain("font.bold=true");
        hiero.ShouldContain("font.italic=true");
        hiero.ShouldContain("render_type=2");
        hiero.ShouldContain("ColorEffect");
    }

    [Fact]
    public void Write_HardShadow_WritesBlurZero()
    {
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                ShadowOffsetX = 2,
                ShadowOffsetY = 2,
                ShadowBlur = 5,
                HardShadow = true
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("ShadowEffect");
        hiero.ShouldContain("effect.Blur kernel size=0");
    }

    [Fact]
    public void Write_OutlineFloatFormat()
    {
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions { Outline = 2, OutlineR = 0x10, OutlineG = 0x20, OutlineB = 0x30 }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("OutlineEffect");
        hiero.ShouldContain("effect.Width=2.0");
        hiero.ShouldContain("effect.Color=102030");
    }

    // ------------------------------------------------------------------
    // Round-trip: parse -> write -> parse
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_MappableProperties_Preserved()
    {
        var original = new BmfcConfig
        {
            FontName = "Arial",
            Options = new FontGeneratorOptions
            {
                Size = 36,
                Bold = true,
                Italic = false,
                AntiAlias = AntiAliasMode.None,
                MaxTextureWidth = 256,
                MaxTextureHeight = 512,
                Padding = new Padding(1, 2, 3, 4),
                Characters = CharacterSet.FromChars("ABCabc123"),
                Outline = 2,
                OutlineR = 0xff,
                GradientStartR = 0x00, GradientStartG = 0xff, GradientStartB = 0xff,
                GradientEndR = 0x00, GradientEndG = 0x00, GradientEndB = 0xff,
                ShadowOffsetX = 3, ShadowOffsetY = 4,
                ShadowR = 0x11, ShadowG = 0x22, ShadowB = 0x33,
                Sdf = false
            }
        };

        var hiero = HieroConfigWriter.Write(original);
        var parsed = HieroConfigReader.Parse(hiero);

        parsed.FontName.ShouldBe("Arial");
        parsed.Options.Size.ShouldBe(36f);
        parsed.Options.Bold.ShouldBeTrue();
        parsed.Options.Italic.ShouldBeFalse();
        parsed.Options.AntiAlias.ShouldBe(AntiAliasMode.None);
        parsed.Options.MaxTextureWidth.ShouldBe(256);
        parsed.Options.MaxTextureHeight.ShouldBe(512);
        parsed.Options.Padding.ShouldBe(new Padding(1, 2, 3, 4));
        parsed.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(original.Options.Characters.GetCodepoints().ToList());
        parsed.Options.Outline.ShouldBe(2);
        parsed.Options.OutlineR.ShouldBe((byte)0xff);
        parsed.Options.GradientStartG.ShouldBe((byte)0xff);
        parsed.Options.GradientEndB.ShouldBe((byte)0xff);
        parsed.Options.ShadowOffsetX.ShouldBe(3);
        parsed.Options.ShadowOffsetY.ShouldBe(4);
        parsed.Options.ShadowR.ShouldBe((byte)0x11);
    }

    // ------------------------------------------------------------------
    // ConfigFormatFactory auto-detection
    // ------------------------------------------------------------------

    [Fact]
    public void Factory_ReadConfig_DetectsHiero()
    {
        // Discriminating: Hiero content but a MISMATCHED .bmfc extension. If detection ever
        // regressed to extension-only this would route to the BMFont reader (which cannot parse
        // glyph.text), so the Hiero-only glyph.text=AB mapping proves content detection won.
        var content = "font.name=Arial\nfont.size=32\nglyph.text=AB\n";
        var path = CreateTempFile("test.bmfc", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        config.FontName.ShouldBe("Arial");
        config.Options.Size.ShouldBe(32f);
        // glyph.text=AB -> A,B is a Hiero-only mapping; BMFont would not produce these codepoints.
        // This proves Hiero content was detected despite the mismatched .bmfc extension.
        config.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B' });
    }

    [Fact]
    public void Factory_ReadConfig_DetectsBmfc()
    {
        // Discriminating: BMFont content but a MISMATCHED .hiero extension. The BMFont-only
        // chars=65-67 range syntax (the Hiero reader cannot parse it) proves content detection won.
        var content = "fontName=Arial\nfontSize=32\nchars=65-67\n";
        var path = CreateTempFile("test.hiero", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        config.FontName.ShouldBe("Arial");
        config.Options.Size.ShouldBe(32f);
        // chars=65-67 -> A,B,C is BMFont range syntax; detection must have chosen the BMFont reader
        // despite the mismatched .hiero extension. The Hiero reader cannot parse range syntax.
        config.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
    }

    [Fact]
    public void Factory_ReadConfig_UnknownExtension_ParsedAsBmfc()
    {
        // Lenient behavior: a non-.hiero extension (here .cfg) is read as BMFont (.bmfc),
        // preserving the historical "any path is .bmfc" contract. No BmFontException.
        var content = "fontName=Arial\nfontSize=32\nchars=65-67\n";
        var path = CreateTempFile("x.cfg", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        config.FontName.ShouldBe("Arial");
        config.Options.Size.ShouldBe(32f);
    }

    [Fact]
    public void Factory_ReadConfig_NoExtension_ParsedAsBmfc()
    {
        // An extensionless config path is also treated as BMFont.
        var content = "fontName=Calibri\nfontSize=18\nchars=65-67\n";
        var path = CreateTempFile("config", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        config.FontName.ShouldBe("Calibri");
        config.Options.Size.ShouldBe(18f);
    }

    [Fact]
    public void Factory_ReadConfig_ExtensionIsCaseInsensitive()
    {
        var content = "font.name=Arial\nfont.size=20\nglyph.text=A\n";
        var path = CreateTempFile("test.HIERO", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        config.Options.Size.ShouldBe(20f);
    }

    [Fact]
    public void Factory_WriteConfig_DetectsHiero()
    {
        var config = new BmfcConfig { FontName = "Arial", Options = new FontGeneratorOptions { Size = 32 } };
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "out.hiero");

        ConfigFormatFactory.WriteConfig(config, path);

        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(path).ShouldContain("font.name=Arial");
    }

    [Fact]
    public void Factory_WriteConfig_UnknownExtension_WritesBmfc()
    {
        // Lenient behavior: a non-.hiero extension (here .cfg) is written as BMFont (.bmfc).
        var config = new BmfcConfig { FontName = "Arial", Options = new FontGeneratorOptions { Size = 32 } };
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "out.cfg");

        ConfigFormatFactory.WriteConfig(config, path);

        File.Exists(path).ShouldBeTrue();
        var written = File.ReadAllText(path);
        // BMFont content uses the bmfc key style, not the Hiero font.* style.
        written.ShouldContain("fileVersion=1");
        written.ShouldContain("fontName=Arial");
        written.ShouldNotContain("render_type=2");
    }

    [Fact]
    public void Factory_WriteConfig_HieroExtensionWithTrailingSpace_WritesHieroContent()
    {
        // L4: a trailing space on a ".hiero " path must still route to the Hiero writer.
        // WriteConfig trims the extension before comparing, so the file is written as Hiero
        // (not BMFont). The on-disk file name itself keeps the trailing space.
        var config = new BmfcConfig { FontName = "Arial", Options = new FontGeneratorOptions { Size = 32 } };
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "x.hiero "); // note the trailing space

        ConfigFormatFactory.WriteConfig(config, path);

        // A trailing space must not demote .hiero to BMFont: the content stays Hiero.
        var written = File.ReadAllText(path);
        written.ShouldContain("render_type=2");
        written.ShouldContain("font.name=Arial");
        written.ShouldNotContain("fileVersion=1");
    }

    [Fact]
    public void Factory_WriteConfig_HieroExtension_StillRoutesToHiero()
    {
        // Confirm .hiero still selects the Hiero format despite the lenient default.
        var config = new BmfcConfig { FontName = "Arial", Options = new FontGeneratorOptions { Size = 32 } };
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "routed.hiero");

        ConfigFormatFactory.WriteConfig(config, path);

        var written = File.ReadAllText(path);
        written.ShouldContain("render_type=2");
        written.ShouldContain("font.name=Arial");
    }

    // ------------------------------------------------------------------
    // Content sniffing: ConfigFormatFactory.ReadConfig detects the format from
    // file CONTENT (extension is only a tiebreaker for inconclusive content).
    // These prove content OVERRIDES extension. Black-box via ReadConfig.
    // ------------------------------------------------------------------

    private const string HieroContent =
        "font.name=Arial\n" +
        "font.size=48\n" +
        "glyph.text=ABC\n" +
        "effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineEffect\n" +
        "effect.Color=ff0000\n" +
        "effect.Width=3.0\n";

    private const string BmfcContent =
        "# AngelCode Bitmap Font Generator configuration file\n" +
        "fileVersion=1\n" +
        "fontName=Arial\n" +
        "fontSize=32\n" +
        "chars=65-67\n";

    /// <summary>Asserts a config was parsed by the Hiero reader, using Hiero-only mappings.</summary>
    private static void ShouldBeParsedAsHiero(BmfcConfig config)
    {
        // glyph.text=ABC -> A,B,C is a Hiero-only mapping (BMFont uses chars=65-67 ranges).
        config.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
        // The OutlineEffect (effect.class=com.badlogic.gdx...) only round-trips through the Hiero reader.
        config.Options.Outline.ShouldBe(3, "Hiero OutlineEffect width 3.0 must map to Outline=3");
        config.Options.OutlineR.ShouldBe((byte)0xff, "Hiero effect.Color=ff0000 must map to OutlineR=0xff");
    }

    /// <summary>Asserts a config was parsed by the BMFont reader, using BMFont-only mappings.</summary>
    private static void ShouldBeParsedAsBmfc(BmfcConfig config)
    {
        config.FontName.ShouldBe("Arial");
        config.Options.Size.ShouldBe(32f);
        // chars=65-67 -> A,B,C is a BMFont range syntax; the Hiero reader would not parse it.
        config.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
        // The BMFont content carries no effect block, so no outline.
        config.Options.Outline.ShouldBe(0, "BMFont content has no effect block; Outline must stay 0");
    }

    [Fact]
    public void ReadConfig_HieroContentInBmfcExtension_ParsedAsHiero()
    {
        // The extension says .bmfc, but the CONTENT is Hiero. Content must win.
        var path = CreateTempFile("mislabeled.bmfc", HieroContent);

        var config = ConfigFormatFactory.ReadConfig(path);

        ShouldBeParsedAsHiero(config);
    }

    [Theory]
    [InlineData("data.txt")]
    [InlineData("data.cfg")]
    [InlineData("data")] // no extension
    public void ReadConfig_HieroContentInNonHieroExtension_ParsedAsHiero(string fileName)
    {
        // Hiero content in a .txt / .cfg / extensionless file is still parsed as Hiero.
        var path = CreateTempFile(fileName, HieroContent);

        var config = ConfigFormatFactory.ReadConfig(path);

        ShouldBeParsedAsHiero(config);
    }

    [Fact]
    public void ReadConfig_BmfcContentInHieroExtension_ParsedAsBmfc()
    {
        // The extension says .hiero, but the CONTENT is BMFont. Content must win.
        var path = CreateTempFile("mislabeled.hiero", BmfcContent);

        var config = ConfigFormatFactory.ReadConfig(path);

        ShouldBeParsedAsBmfc(config);
    }

    [Theory]
    [InlineData("data.cfg")]
    [InlineData("data")] // no extension
    public void ReadConfig_BmfcContentInNonBmfcExtension_ParsedAsBmfc(string fileName)
    {
        // Pre-existing lenient behavior: BMFont content in a .cfg / extensionless file reads as BMFont.
        var path = CreateTempFile(fileName, BmfcContent);

        var config = ConfigFormatFactory.ReadConfig(path);

        ShouldBeParsedAsBmfc(config);
    }

    [Fact]
    public void ReadConfig_BmfcWithBadlogicInOutputPathValue_ParsedAsBmfc()
    {
        // M1 regression (black-box): a genuine BMFont .bmfc whose outputPath VALUE contains
        // "com.badlogic.gdx" (a libGDX project tree) must NOT be misrouted to the Hiero reader.
        // It is read as BMFont, so the BMFont-only chars=65-67 range syntax parses to A,B,C.
        var content =
            "# AngelCode Bitmap Font Generator configuration file\n" +
            "fileVersion=1\n" +
            "fontFile=Arial.ttf\n" +
            "fontName=Arial\n" +
            "fontSize=32\n" +
            "chars=65-67\n" +
            "outputPath=/projects/com.badlogic.gdx.demo/assets/font\n";
        var path = CreateTempFile("badlogic-value.bmfc", content);

        var config = ConfigFormatFactory.ReadConfig(path);

        ShouldBeParsedAsBmfc(config);
    }

    [Fact]
    public void ReadConfig_AmbiguousContentHieroExtension_FallsBackToHiero()
    {
        // Content with no decisive signals -> fall back to the .hiero extension -> Hiero reader.
        var content = "# just a comment\nthis line has no equals sign\n";
        var path = CreateTempFile("ambiguous.hiero", content);

        // White-box: prove the content really is inconclusive, so the .hiero extension (not a
        // content signal) is what routes to the Hiero reader. Asserting Size==32f alone cannot
        // distinguish routing because both readers default Size to 32f.
        ConfigFormatDetector.DetectFromContent(content)
            .ShouldBe(DetectedConfigFormat.Unknown, "the test content must carry no decisive format signal");

        var config = ConfigFormatFactory.ReadConfig(path);

        // The Hiero reader produces a default config without throwing.
        config.ShouldNotBeNull();
        config.Options.Size.ShouldBe(32f, "ambiguous .hiero content falls back to the Hiero reader's defaults");
    }

    [Fact]
    public void ReadConfig_EmptyContentBmfcExtension_FallsBackToBmfc()
    {
        // Empty content is inconclusive; a non-.hiero extension falls back to BMFont.
        // White-box: prove empty content detects as Unknown (Size==32f alone proves nothing,
        // since both readers default to 32f).
        ConfigFormatDetector.DetectFromContent("")
            .ShouldBe(DetectedConfigFormat.Unknown, "empty content carries no format signal");

        var path = CreateTempFile("empty.bmfc", "");

        var config = ConfigFormatFactory.ReadConfig(path);

        config.ShouldNotBeNull();
        config.Options.ShouldNotBeNull();
    }

    [Fact]
    public void ReadConfig_EmptyContentOtherExtension_FallsBackToBmfc()
    {
        // Empty content with a non-.hiero extension (.cfg) falls back to BMFont.
        // White-box: prove empty content detects as Unknown so the extension is the tiebreaker.
        ConfigFormatDetector.DetectFromContent("")
            .ShouldBe(DetectedConfigFormat.Unknown, "empty content carries no format signal");

        var path = CreateTempFile("empty.cfg", "");

        var config = ConfigFormatFactory.ReadConfig(path);

        config.ShouldNotBeNull();
        config.Options.ShouldNotBeNull();
    }

    // ------------------------------------------------------------------
    // L6: missing config file throws FileNotFoundException (not Directory*)
    // ------------------------------------------------------------------

    [Fact]
    public void ReadConfig_MissingFileInExistingDir_ThrowsFileNotFound()
    {
        // Use an existing temp dir but a file name that was never written.
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var missing = Path.Combine(dir, "does-not-exist.bmfc");

        var ex = Should.Throw<FileNotFoundException>(() => ConfigFormatFactory.ReadConfig(missing));
        ex.Message.ShouldContain("Config file not found");
    }

    [Fact]
    public void FromConfig_MissingFileInExistingDir_ThrowsFileNotFound()
    {
        // BmFont.FromConfig routes through ConfigFormatFactory.ReadConfig, so a missing file
        // must surface the same friendly FileNotFoundException, not a DirectoryNotFoundException.
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var missing = Path.Combine(dir, "does-not-exist.hiero");

        var ex = Should.Throw<FileNotFoundException>(() => BmFont.FromConfig(missing));
        ex.Message.ShouldContain("Config file not found");
    }

    // ------------------------------------------------------------------
    // ConfigFormatDetector unit tests (white-box; internal type is visible
    // to KernSmith.Tests via InternalsVisibleTo).
    // ------------------------------------------------------------------

    [Fact]
    public void DetectFromContent_HieroDottedKeys_ReturnsHiero()
    {
        ConfigFormatDetector.DetectFromContent("font.size=48\nglyph.text=ABC\n")
            .ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_BadlogicNamespace_IsDefinitiveHiero()
    {
        // The libGDX namespace is a single-line definitive Hiero signal even amid BMFont keys.
        var content =
            "fontName=Arial\nfontSize=32\n" +
            "effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.ColorEffect\n";
        ConfigFormatDetector.DetectFromContent(content).ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_BadlogicNamespaceOnlyInValue_IsBmfc()
    {
        // M1 regression: "com.badlogic.gdx" appears ONLY inside a BMFont value (an outputPath
        // into a libGDX project tree), never on an effect.class= line. The detector must scope
        // its libGDX check to effect.class= lines, so this is BMFont (camelCase keys win),
        // NOT a false-positive Hiero.
        var content =
            "fileVersion=1\n" +
            "fontFile=Arial.ttf\n" +
            "fontSize=32\n" +
            "outputPath=/projects/com.badlogic.gdx.demo/assets/font\n";
        ConfigFormatDetector.DetectFromContent(content).ShouldBe(DetectedConfigFormat.Bmfc);
    }

    [Fact]
    public void DetectFromContent_BadlogicNamespaceInEffectClassLine_IsStillHiero()
    {
        // M1 companion: a REAL Hiero effect.class= line carrying the libGDX namespace must
        // still be detected as Hiero, even when BMFont-looking camelCase keys precede it.
        var content =
            "outputPath=/projects/com.badlogic.gdx.demo/assets/font\n" +
            "effect.class=com.badlogic.gdx.tools.hiero.unicodefont.effects.OutlineEffect\n";
        ConfigFormatDetector.DetectFromContent(content).ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_RenderType_IsHieroSignal()
    {
        ConfigFormatDetector.DetectFromContent("render_type=2\n").ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Fact]
    public void DetectFromContent_BmfcCamelCaseKeys_ReturnsBmfc()
    {
        ConfigFormatDetector.DetectFromContent("fontName=Arial\nfontSize=32\nchars=65-67\n")
            .ShouldBe(DetectedConfigFormat.Bmfc);
    }

    [Fact]
    public void DetectFromContent_AngelCodeHeaderComment_IsBmfcSignal()
    {
        ConfigFormatDetector.DetectFromContent(
            "# AngelCode Bitmap Font Generator configuration file\nfontSize=32\n")
            .ShouldBe(DetectedConfigFormat.Bmfc);
    }

    [Fact]
    public void DetectFromContent_FontSizeVsFontDotSize_Disambiguates()
    {
        // The core discriminator: "fontSize" (BMFont) vs "font.size" (Hiero).
        ConfigFormatDetector.DetectFromContent("fontSize=32\n").ShouldBe(DetectedConfigFormat.Bmfc);
        ConfigFormatDetector.DetectFromContent("font.size=32\n").ShouldBe(DetectedConfigFormat.Hiero);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# just a comment\n")]
    [InlineData("no equals here\nstill no equals\n")]
    [InlineData("unknownKey=1\nanother.unknown.key=2\n")]
    public void DetectFromContent_NoDecisiveSignals_ReturnsUnknown(string content)
    {
        ConfigFormatDetector.DetectFromContent(content).ShouldBe(DetectedConfigFormat.Unknown);
    }

    [Fact]
    public void DetectFromContent_EqualSignalCounts_ReturnsUnknown()
    {
        // One BMFont key and one Hiero key cancel out -> Unknown (caller uses extension).
        ConfigFormatDetector.DetectFromContent("fontSize=32\nfont.size=48\n")
            .ShouldBe(DetectedConfigFormat.Unknown);
    }

    // ------------------------------------------------------------------
    // Read resolves relative font path against config directory
    // ------------------------------------------------------------------

    [Fact]
    public void Read_ResolvesRelativeFontPath()
    {
        var content =
            "font2.file=sub/MyFont.ttf\n" +
            "font2.use=true\n";
        var path = CreateTempFile("test.hiero", content);

        var config = HieroConfigReader.Read(path);

        Path.IsPathRooted(config.FontFile).ShouldBeTrue();
        config.FontFile!.ShouldContain("MyFont.ttf");
    }

    // ------------------------------------------------------------------
    // Integration: BmFont.FromConfig + ToHiero
    // ------------------------------------------------------------------

    [Fact]
    public void FromConfig_HieroPath_GeneratesFont()
    {
        var content =
            $"font2.file={TestFontPath}\n" +
            "font2.use=true\n" +
            "font.size=32\n" +
            "glyph.text=ABCabc\n";
        var path = CreateTempFile("test.hiero", content);

        var result = BmFont.FromConfig(path);

        result.Model.Characters.Count.ShouldBeGreaterThan(0);
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ToHiero_RoundTripsThroughParse()
    {
        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABC"),
            Outline = 2,
            Bold = true
        });

        var hiero = result.ToHiero();
        var parsed = HieroConfigReader.Parse(hiero);

        parsed.Options.Size.ShouldBe(32f);
        parsed.Options.Bold.ShouldBeTrue();
        parsed.Options.Outline.ShouldBe(2);
        parsed.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
    }

    [Fact]
    public void ToHiero_WithoutSourceOptions_Throws()
    {
        // Load() produces a result with no SourceOptions.
        var generated = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A")
        });
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var fntBase = Path.Combine(dir, "font");
        generated.ToFile(fntBase);

        var loaded = BmFont.Load(fntBase + ".fnt");

        Should.Throw<InvalidOperationException>(() => loaded.ToHiero());
    }

    // ------------------------------------------------------------------
    // #5 / #12: malformed, negative, and edge-case input handling
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_NonNumericFontSize_KeepsDefaultButParsesOtherKeys()
    {
        var content =
            "font.name=Verdana\n" +
            "font.size=abc\n" +
            "font.bold=true\n";

        var config = HieroConfigReader.Parse(content);

        // Bad font.size is dropped with a warning, leaving the 32f default in place.
        config.Options.Size.ShouldBe(32f, "non-numeric font.size must not corrupt the default size");
        // Other keys on surrounding lines still parse normally.
        config.FontName.ShouldBe("Verdana");
        config.Options.Bold.ShouldBeTrue();
    }

    [Fact]
    public void Parse_OutlineEffect_BadHexColor_FallsBackToWhite()
    {
        // Valid Width keeps the outline alive; a short/invalid hex color falls back to WHITE.
        var content =
            $"effect.class={EffectPkg}OutlineEffect\n" +
            "effect.Color=xyz\n" +
            "effect.Width=2.0\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(2);
        config.Options.OutlineR.ShouldBe((byte)255);
        config.Options.OutlineG.ShouldBe((byte)255);
        config.Options.OutlineB.ShouldBe((byte)255);
    }

    [Fact]
    public void Parse_BlankLinesAndLinesWithoutEquals_AreIgnored()
    {
        var content =
            "\n" +
            "this line has no equals sign\n" +
            "font.size=40\n" +
            "   \n" +
            "another bare line\n";

        var config = HieroConfigReader.Parse(content);

        // The single valid key=value line is still parsed; junk lines are skipped.
        config.Options.Size.ShouldBe(40f);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsDefaultOptions()
    {
        var config = HieroConfigReader.Parse("");

        config.ShouldNotBeNull();
        config.Options.ShouldNotBeNull();
        config.Options.Size.ShouldBe(32f);
        config.FontName.ShouldBeNull();
        config.Options.Outline.ShouldBe(0);
    }

    [Fact]
    public void Parse_BomPrefixedContent_ParsesFirstKey()
    {
        // A leading UTF-8 BOM (U+FEFF) must be stripped so the first key is not "﻿font.size".
        var content = "﻿" + "font.size=64\n" + "font.bold=true\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Size.ShouldBe(64f, "the BOM must not prevent the first key from parsing");
        config.Options.Bold.ShouldBeTrue();
    }

    // ------------------------------------------------------------------
    // #15: shadow opacity/blur round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_ShadowOpacityBlurAndColor_Preserved()
    {
        var original = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                ShadowOffsetX = 2,
                ShadowOffsetY = 2,
                ShadowOpacity = 0.6f,
                ShadowBlur = 4,
                ShadowR = 0x11,
                ShadowG = 0x22,
                ShadowB = 0x33
            }
        };

        var hiero = HieroConfigWriter.Write(original);

        // The 0.6 opacity must be serialized literally (formatted as "0.6").
        hiero.ShouldContain("effect.Opacity=0.6");

        var parsed = HieroConfigReader.Parse(hiero);

        parsed.Options.ShadowOpacity.ShouldBe(0.6f);
        parsed.Options.ShadowBlur.ShouldBe(4);
        parsed.Options.ShadowR.ShouldBe((byte)0x11);
        parsed.Options.ShadowG.ShouldBe((byte)0x22);
        parsed.Options.ShadowB.ShouldBe((byte)0x33);
    }

    // ------------------------------------------------------------------
    // #1: shadow blur kernel snapping to legal Hiero values
    // ------------------------------------------------------------------

    [Fact]
    public void Write_ShadowBlurOne_SnappedToTwo()
    {
        // Hiero "Blur kernel size" of 1 is illegal; a 1px blur snaps up to 2.
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                ShadowOffsetX = 1,
                ShadowBlur = 1,
                HardShadow = false
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("ShadowEffect");
        hiero.ShouldContain("effect.Blur kernel size=2");
    }

    [Fact]
    public void Write_ShadowBlurThree_WrittenAsThree()
    {
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                ShadowBlur = 3,
                HardShadow = false
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("ShadowEffect");
        hiero.ShouldContain("effect.Blur kernel size=3");
    }

    [Fact]
    public void Write_HardShadowWithBlur_WritesKernelZero()
    {
        // HardShadow has no Hiero equivalent: kernel size is forced to 0 regardless of ShadowBlur.
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                ShadowOffsetX = 2,
                ShadowBlur = 5,
                HardShadow = true
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("ShadowEffect");
        hiero.ShouldContain("effect.Blur kernel size=0");
    }

    // ------------------------------------------------------------------
    // #2: backslash handling in glyph.text / character set.
    // Matching real Hiero (HieroSettings.java): ONLY newlines escape as "\n";
    // a backslash is a literal char, NOT an escape introducer.
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_CharacterSetWithBackslash_Preserved()
    {
        var original = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                Characters = CharacterSet.FromChars("A\\B/C")
            }
        };

        var hiero = HieroConfigWriter.Write(original);
        var parsed = HieroConfigReader.Parse(hiero);

        var codepoints = parsed.Options.Characters.GetCodepoints().ToList();
        codepoints.ShouldContain((int)'A');
        codepoints.ShouldContain((int)'\\');
        codepoints.ShouldContain((int)'B');
        codepoints.ShouldContain((int)'/');
        codepoints.ShouldContain((int)'C');
        // A literal backslash must NOT have been corrupted into a newline.
        codepoints.ShouldNotContain((int)'\n');
    }

    [Fact]
    public void Write_CharacterSetWithBackslash_EmitsSingleLiteralBackslash()
    {
        // Interop with real Hiero (HieroSettings.java): a backslash in the glyph set is written
        // as a SINGLE literal '\' (NOT escaped to "\\"). Asserting the exact glyph.text line guards
        // against re-introducing the non-standard backslash-escaping scheme.
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                Characters = CharacterSet.FromChars("A\\B/C")
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        // GetCodepoints() sorts ascending: '/'(0x2F) 'A'(0x41) 'B'(0x42) 'C'(0x43) '\'(0x5C),
        // so the literal glyph.text is "/ABC\" with a SINGLE trailing backslash.
        hiero.ShouldContain("glyph.text=/ABC\\");
        // The non-standard double-backslash escaping must NOT be present.
        hiero.ShouldNotContain("\\\\");
    }

    [Fact]
    public void Parse_GlyphTextLiteralBackslash_NotTurnedIntoNewline()
    {
        // Matching real Hiero, a backslash is NOT an escape introducer: a backslash followed by a
        // non-'n' char (here 't') is emitted verbatim, never decoded and never turned into a newline.
        var content = "glyph.text=A\\t\n"; // glyph.text=A\t

        var config = HieroConfigReader.Parse(content);

        var codepoints = config.Options.Characters.GetCodepoints().ToList();
        codepoints.ShouldContain((int)'A');
        codepoints.ShouldContain((int)'\\');
        codepoints.ShouldContain((int)'t');
        codepoints.ShouldNotContain((int)'\n');
    }

    // ------------------------------------------------------------------
    // #7: sub-pixel outline width
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_OutlineEffect_SubPixelWidth_BumpedToOne()
    {
        // A thin sub-pixel Width (0.4) rounds to 0 but is bumped up to 1 so the outline
        // (and its color) is not silently dropped.
        var content =
            $"effect.class={EffectPkg}OutlineEffect\n" +
            "effect.Color=abcdef\n" +
            "effect.Width=0.4\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(1);
        config.Options.OutlineR.ShouldBe((byte)0xab);
        config.Options.OutlineG.ShouldBe((byte)0xcd);
        config.Options.OutlineB.ShouldBe((byte)0xef);
    }

    // ------------------------------------------------------------------
    // #16: gradient writer with asymmetric / 6-channel colors
    // ------------------------------------------------------------------

    [Fact]
    public void Write_GradientEffect_AsymmetricColors_EmitsTopAndBottom()
    {
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                GradientStartR = 0x11, GradientStartG = 0x22, GradientStartB = 0x33,
                GradientEndR = 0x44, GradientEndG = 0x55, GradientEndB = 0x66
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        hiero.ShouldContain("GradientEffect");
        hiero.ShouldContain("effect.Top color=112233");
        hiero.ShouldContain("effect.Bottom color=445566");
    }

    [Fact]
    public void RoundTrip_GradientSixChannels_DistinctValuesPreserved()
    {
        var original = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                GradientStartR = 0x11, GradientStartG = 0x22, GradientStartB = 0x33,
                GradientEndR = 0x44, GradientEndG = 0x55, GradientEndB = 0x66
            }
        };

        var hiero = HieroConfigWriter.Write(original);
        var parsed = HieroConfigReader.Parse(hiero);

        parsed.Options.GradientStartR.ShouldBe((byte)0x11);
        parsed.Options.GradientStartG.ShouldBe((byte)0x22);
        parsed.Options.GradientStartB.ShouldBe((byte)0x33);
        parsed.Options.GradientEndR.ShouldBe((byte)0x44);
        parsed.Options.GradientEndG.ShouldBe((byte)0x55);
        parsed.Options.GradientEndB.ShouldBe((byte)0x66);
    }

    // ------------------------------------------------------------------
    // #17: documented warn/drop branches do not corrupt unrelated options
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_ColorEffectNonWhiteFill_IgnoredOutlineGradientStayDefault()
    {
        var content =
            $"effect.class={EffectPkg}ColorEffect\n" +
            "effect.Color=ff0000\n";

        var config = HieroConfigReader.Parse(content);

        // Non-white fill is ignored on import; it must not enable outline or gradient.
        config.Options.Outline.ShouldBe(0);
        config.Options.GradientStartR.ShouldBeNull();
        config.Options.GradientEndR.ShouldBeNull();
    }

    [Fact]
    public void Parse_GradientEffect_OnlyTopColor_DoesNotSetGradient()
    {
        // The gradient requires BOTH Top and Bottom colors; with only Top present it is dropped.
        var content =
            $"effect.class={EffectPkg}GradientEffect\n" +
            "effect.Top color=00ff00\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.GradientStartR.ShouldBeNull();
        config.Options.GradientEndR.ShouldBeNull();
    }

    [Fact]
    public void Parse_OutlineZigzagEffect_Skipped()
    {
        var content =
            $"effect.class={EffectPkg}OutlineZigzagEffect\n" +
            "effect.Width=4.0\n" +
            "effect.Amplitude=2\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(0, "OutlineZigzagEffect has no KernSmith equivalent and must be skipped");
    }

    [Fact]
    public void Parse_NonFreeTypeRenderTypeAndGamma_DoNotThrowOrChangeOptions()
    {
        var content =
            "render_type=0\n" +
            "font.gamma=2.2\n" +
            "font.size=28\n";

        var config = HieroConfigReader.Parse(content);

        // render_type != 2 and font.gamma are warned/dropped but harmless: surrounding keys parse,
        // and unrelated options keep their defaults.
        config.Options.Size.ShouldBe(28f);
        config.Options.Outline.ShouldBe(0);
        config.Options.Sdf.ShouldBeFalse();
        config.Options.Bold.ShouldBeFalse();
    }

    // ------------------------------------------------------------------
    // #18: SDF round-trip and canonical effect ordering
    // ------------------------------------------------------------------

    [Fact]
    public void RoundTrip_Sdf_Preserved()
    {
        var original = new BmfcConfig { Options = new FontGeneratorOptions { Sdf = true } };

        var hiero = HieroConfigWriter.Write(original);
        var parsed = HieroConfigReader.Parse(hiero);

        parsed.Options.Sdf.ShouldBeTrue();
    }

    [Fact]
    public void Write_AllEffectsEnabled_EmittedInCanonicalOrder()
    {
        var config = new BmfcConfig
        {
            Options = new FontGeneratorOptions
            {
                Outline = 2,
                OutlineR = 0xff,
                GradientStartR = 0x00, GradientStartG = 0xff, GradientStartB = 0xff,
                GradientEndR = 0x00, GradientEndG = 0x00, GradientEndB = 0xff,
                ShadowOffsetX = 2, ShadowOffsetY = 2,
                Sdf = true
            }
        };

        var hiero = HieroConfigWriter.Write(config);

        int colorIdx = hiero.IndexOf(EffectPkg + "ColorEffect", StringComparison.Ordinal);
        int outlineIdx = hiero.IndexOf(EffectPkg + "OutlineEffect", StringComparison.Ordinal);
        int gradientIdx = hiero.IndexOf(EffectPkg + "GradientEffect", StringComparison.Ordinal);
        int shadowIdx = hiero.IndexOf(EffectPkg + "ShadowEffect", StringComparison.Ordinal);
        int sdfIdx = hiero.IndexOf(EffectPkg + "DistanceFieldEffect", StringComparison.Ordinal);

        colorIdx.ShouldBeGreaterThanOrEqualTo(0);
        outlineIdx.ShouldBeGreaterThanOrEqualTo(0);
        gradientIdx.ShouldBeGreaterThanOrEqualTo(0);
        shadowIdx.ShouldBeGreaterThanOrEqualTo(0);
        sdfIdx.ShouldBeGreaterThanOrEqualTo(0);

        colorIdx.ShouldBeLessThan(outlineIdx);
        outlineIdx.ShouldBeLessThan(gradientIdx);
        gradientIdx.ShouldBeLessThan(shadowIdx);
        shadowIdx.ShouldBeLessThan(sdfIdx);
    }

    // ------------------------------------------------------------------
    // #21: shadow opacity default when Opacity key is absent
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_ShadowEffect_NoOpacityKey_DefaultsToSixTenths()
    {
        // A ShadowEffect block without an Opacity key adopts Hiero's documented 0.6 default
        // (REF-10), not KernSmith's 1.0 default.
        var content =
            $"effect.class={EffectPkg}ShadowEffect\n" +
            "effect.Color=000000\n" +
            "effect.X distance=2.0\n" +
            "effect.Y distance=2.0\n" +
            "effect.Blur kernel size=2\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.ShadowOpacity.ShouldBe(0.6f);
    }

    // ------------------------------------------------------------------
    // P82-4 / P82-5: REF-10 effect defaults applied when block is present
    // but the corresponding key is absent.
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_OutlineEffect_NoWidthKey_DefaultsToTwo()
    {
        // An OutlineEffect block without a Width key adopts Hiero's documented Width default of 2
        // (REF-10), not KernSmith's 0 default. The color must still apply since Outline > 0.
        var content =
            $"effect.class={EffectPkg}OutlineEffect\n" +
            "effect.Color=ff0000\n" +
            "effect.Join=0\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.Outline.ShouldBe(2);
        config.Options.OutlineR.ShouldBe((byte)0xff);
        config.Options.OutlineG.ShouldBe((byte)0x00);
        config.Options.OutlineB.ShouldBe((byte)0x00);
    }

    [Fact]
    public void Parse_ShadowEffect_NoDistanceKeys_DefaultsToTwo()
    {
        // A ShadowEffect block omitting X/Y distance adopts Hiero's documented distance default
        // of 2 (REF-10), not KernSmith's 0 default. Blur stays at its 0 default.
        var content =
            $"effect.class={EffectPkg}ShadowEffect\n" +
            "effect.Color=000000\n" +
            "effect.Opacity=0.6\n";

        var config = HieroConfigReader.Parse(content);

        config.Options.ShadowOffsetX.ShouldBe(2);
        config.Options.ShadowOffsetY.ShouldBe(2);
        config.Options.ShadowBlur.ShouldBe(0);
    }

    [Fact]
    public void WriteToFile_ThenReadConfig_DetectedAsHiero()
    {
        // Self-round-trip detection guard: a file written by HieroConfigWriter must stay
        // detectable as Hiero by ConfigFormatFactory's content sniffing. The Hiero-only
        // glyph.text=ABC -> A,B,C mapping proves the Hiero reader (not BMFont) handled it.
        var config = new BmfcConfig
        {
            FontName = "Arial",
            Options = new FontGeneratorOptions
            {
                Size = 32,
                Characters = CharacterSet.FromChars("ABC")
            }
        };
        var dir = Path.Combine(Path.GetTempPath(), $"KernSmith_HieroTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempPaths.Add(dir);
        var path = Path.Combine(dir, "selfroundtrip.hiero");

        HieroConfigWriter.WriteToFile(config, path);
        var parsed = ConfigFormatFactory.ReadConfig(path);

        // glyph.text codepoints survived -> the writer's emitted keys were sniffed as Hiero.
        parsed.Options.Characters.GetCodepoints().ToList()
            .ShouldBe(new[] { (int)'A', (int)'B', (int)'C' });
        parsed.FontName.ShouldBe("Arial");
        parsed.Options.Size.ShouldBe(32f);
    }
}
