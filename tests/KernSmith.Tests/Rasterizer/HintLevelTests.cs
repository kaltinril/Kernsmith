using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 188 — the public <see cref="HintLevel"/> in core: defaults, the <see cref="RasterOptions"/>
/// mapping, the builder, <c>.bmfc</c> key, the contradiction rule with
/// <see cref="FontGeneratorOptions.EnableHinting"/>, and the step-down negotiation against
/// <see cref="IRasterizerCapabilities.SupportedHintLevels"/>.
/// </summary>
[Collection("RasterizerFactory")]
public class HintLevelTests
{
    private static readonly string FontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] Font() => File.ReadAllBytes(FontPath);

    private static readonly HintLevel[] NoneAndLight = [HintLevel.None, HintLevel.Light];
    private static readonly HintLevel[] AllLevels = Enum.GetValues<HintLevel>();

    // ---- Options ------------------------------------------------------------------------------

    [Fact]
    public void Default_IsDefault_OnBothOptionTypes()
    {
        new FontGeneratorOptions().HintLevel.ShouldBe(HintLevel.Default);
        new RasterOptions { Size = 12 }.HintLevel.ShouldBe(HintLevel.Default);
    }

    [Theory]
    [InlineData(HintLevel.None)]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Normal)]
    [InlineData(HintLevel.Full)]
    public void FromGeneratorOptions_CarriesTheLevel(HintLevel level)
    {
        RasterOptions.FromGeneratorOptions(new FontGeneratorOptions { HintLevel = level }).HintLevel.ShouldBe(level);
    }

    [Fact]
    public void Builder_WithHintLevel_AndFromConfig_ReachTheRasterizer()
    {
        var direct = new RecordingRasterizer(AllLevels);
        BmFont.Builder().WithFont(Font()).WithSize(24).WithCharacters(CharacterSet.FromChars("A"))
            .WithRasterizer(direct).WithHintLevel(HintLevel.Normal).Build();

        direct.Seen.ShouldNotBeEmpty();
        direct.Seen.ShouldAllBe(o => o.HintLevel == HintLevel.Normal);

        var config = BmfcConfigReader.Parse($"fontFile={FontPath}\nfontSize=24\nchars=65\nhintLevel=full\n");
        var fromConfig = new RecordingRasterizer(AllLevels);
        BmFont.Builder().FromConfig(config).WithRasterizer(fromConfig).Build();

        fromConfig.Seen.ShouldNotBeEmpty();
        fromConfig.Seen.ShouldAllBe(o => o.HintLevel == HintLevel.Full);
    }

    // ---- .bmfc --------------------------------------------------------------------------------

    [Theory]
    [InlineData("none", HintLevel.None)]
    [InlineData("light", HintLevel.Light)]
    [InlineData("normal", HintLevel.Normal)]
    [InlineData("full", HintLevel.Full)]
    [InlineData("FULL", HintLevel.Full)]
    public void Bmfc_HintLevelKey_IsReadAndRoundTrips(string value, HintLevel expected)
    {
        var parsed = BmfcConfigReader.Parse($"fontSize=32\nhintLevel={value}\n");
        parsed.Options.HintLevel.ShouldBe(expected);

        var written = BmfcConfigWriter.Write(parsed);
        written.ShouldContain($"hintLevel={expected.ToString().ToLowerInvariant()}", Case.Sensitive);
        BmfcConfigReader.Parse(written).Options.HintLevel.ShouldBe(expected);
    }

    [Fact]
    public void Bmfc_DefaultWritesNoHintLevelKey_AndUnknownValuesKeepTheDefault()
    {
        // Default output stays byte-identical to configs written before the key existed, and
        // useHinting keeps carrying EnableHinting on its own.
        var written = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));
        written.ShouldNotContain("hintLevel", Case.Sensitive);
        written.ShouldContain("useHinting=1", Case.Sensitive);

        BmfcConfigReader.Parse("fontSize=32\nhintLevel=bogus\n").Options.HintLevel.ShouldBe(HintLevel.Default);
        BmfcConfigReader.Parse("fontSize=32\nhintLevel=default\n").Options.HintLevel.ShouldBe(HintLevel.Default);
    }

    [Fact]
    public void Bmfc_UseHintingAndHintLevel_AreIndependentKeys()
    {
        var parsed = BmfcConfigReader.Parse("fontSize=32\nuseHinting=0\nhintLevel=none\n").Options;
        parsed.EnableHinting.ShouldBeFalse();
        parsed.HintLevel.ShouldBe(HintLevel.None);

        var written = BmfcConfigWriter.Write(BmfcConfig.FromOptions(parsed));
        written.ShouldContain("useHinting=0", Case.Sensitive);
        written.ShouldContain("hintLevel=none", Case.Sensitive);
    }

    // ---- The contradiction rule ----------------------------------------------------------------

    [Theory]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Normal)]
    [InlineData(HintLevel.Full)]
    public void HintingOff_WithAnExplicitHintedLevel_IsRejected(HintLevel level)
    {
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            Rasterizer = new RecordingRasterizer(AllLevels),
            EnableHinting = false,
            HintLevel = level,
        };

        var ex = Should.Throw<ArgumentException>(() => BmFont.Generate(Font(), options));
        ex.Message.ShouldContain("EnableHinting", Case.Sensitive);
        ex.Message.ShouldContain(level.ToString(), Case.Sensitive);
    }

    [Theory]
    [InlineData(HintLevel.Default)]
    [InlineData(HintLevel.None)]
    public void HintingOff_WithNoneOrDefault_IsAccepted(HintLevel level)
    {
        var recording = new RecordingRasterizer(AllLevels);
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            Rasterizer = recording,
            EnableHinting = false,
            HintLevel = level,
        };

        BmFont.Generate(Font(), options);

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(o => o.HintLevel == level && !o.EnableHinting);
    }

    [Fact]
    public void AtlasSizeQuery_AppliesTheSameRule()
    {
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("A"),
            Rasterizer = new RecordingRasterizer(AllLevels),
            EnableHinting = false,
            HintLevel = HintLevel.Light,
        };

        Should.Throw<ArgumentException>(() => BmFont.QueryAtlasSize(Font(), options));
    }

    // ---- Negotiation policy -------------------------------------------------------------------

    [Theory]
    [InlineData(HintLevel.Default, HintLevel.Default)]
    [InlineData(HintLevel.None, HintLevel.None)]
    [InlineData(HintLevel.Light, HintLevel.Light)]
    [InlineData(HintLevel.Normal, HintLevel.Light)]
    [InlineData(HintLevel.Full, HintLevel.Light)]
    public void UnsupportedLevels_StepDownToTheNearestSupportedOne(HintLevel requested, HintLevel expected)
    {
        HintLevelNegotiation.Resolve(requested, NoneAndLight).ShouldBe(expected);
    }

    [Theory]
    [InlineData(HintLevel.Default)]
    [InlineData(HintLevel.None)]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Normal)]
    [InlineData(HintLevel.Full)]
    public void SupportedLevels_PassThrough(HintLevel requested)
    {
        HintLevelNegotiation.Resolve(requested, AllLevels).ShouldBe(requested);
    }

    [Theory]
    [InlineData(HintLevel.None)]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Normal)]
    [InlineData(HintLevel.Full)]
    public void ABackendAdvertisingNothing_GetsDefault(HintLevel requested)
    {
        HintLevelNegotiation.Resolve(requested, []).ShouldBe(HintLevel.Default);
    }

    [Fact]
    public void StepDown_SkipsOverUnsupportedLevels()
    {
        // Full with only None advertised walks Normal and Light to reach None; Light with only
        // Full advertised has nothing below it and lands on Default.
        HintLevelNegotiation.Resolve(HintLevel.Full, [HintLevel.None]).ShouldBe(HintLevel.None);
        HintLevelNegotiation.Resolve(HintLevel.Full, [HintLevel.Normal]).ShouldBe(HintLevel.Normal);
        HintLevelNegotiation.Resolve(HintLevel.Light, [HintLevel.Full]).ShouldBe(HintLevel.Default);
        HintLevelNegotiation.Resolve(HintLevel.None, [HintLevel.Full]).ShouldBe(HintLevel.Default);
    }

    [Fact]
    public void Resolve_NeverReturnsALevelTheBackendDidNotList()
    {
        var supportSets = new HintLevel[][]
        {
            [],
            [HintLevel.None],
            NoneAndLight,
            [HintLevel.Full],
            [HintLevel.None, HintLevel.Full],
            AllLevels,
        };

        foreach (var supported in supportSets)
        {
            foreach (var requested in AllLevels)
            {
                var effective = HintLevelNegotiation.Resolve(requested, supported);
                (effective == HintLevel.Default || supported.Contains(effective)).ShouldBeTrue(
                    $"{requested} against [{string.Join(", ", supported)}] became {effective}");
                (effective <= requested || effective == HintLevel.Default).ShouldBeTrue(
                    $"{requested} stepped up to {effective}");
            }
        }
    }

    // ---- Where core applies it ----------------------------------------------------------------

    [Theory]
    [InlineData(HintLevel.Full, HintLevel.Light)]
    [InlineData(HintLevel.Normal, HintLevel.Light)]
    [InlineData(HintLevel.Light, HintLevel.Light)]
    [InlineData(HintLevel.None, HintLevel.None)]
    [InlineData(HintLevel.Default, HintLevel.Default)]
    public void Generation_HandsTheBackendTheEffectiveLevel(HintLevel requested, HintLevel expected)
    {
        var recording = new RecordingRasterizer(NoneAndLight);
        var options = new FontGeneratorOptions { Size = 32, Rasterizer = recording, HintLevel = requested };

        using (var prepared = PreparedRasterization.Prepare(
            Font(), options, systemFontName: null, subsetToCharacterSet: true))
        {
            prepared.RasterizeAndProcess(new[] { (int)'A' }, equalizeSpan: null);
        }

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(o => o.HintLevel == expected);
        options.HintLevel.ShouldBe(requested, "the caller's options are not rewritten");
    }

    [Fact]
    public void AtlasSizeQuery_MeasuresWithTheEffectiveLevel()
    {
        var recording = new RecordingRasterizer([]);
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB"),
            Rasterizer = recording,
            HintLevel = HintLevel.Full,
        };

        BmFont.QueryAtlasSize(Font(), options);

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(o => o.HintLevel == HintLevel.Default);
    }

    // ---- The shipped backends -----------------------------------------------------------------

    [Fact]
    public void FreeType_AdvertisesNoneLightAndFull()
    {
        using var freetype = new KernSmith.Rasterizers.FreeType.FreeTypeRasterizer();
        freetype.Capabilities.SupportedHintLevels.ShouldBe([HintLevel.None, HintLevel.Light, HintLevel.Full], ignoreOrder: true);
    }

    [Fact]
    public void StbTrueType_AdvertisesNone()
    {
        using var stb = new KernSmith.Rasterizers.StbTrueType.StbTrueTypeRasterizer();
        stb.Capabilities.SupportedHintLevels.ShouldBe([HintLevel.None]);
    }

#if WINDOWS
    [Fact]
    public void Gdi_AdvertisesFull()
    {
        // GDI always runs the font's bytecode; it has no unhinted path, so Full is its only level.
        using var gdi = new KernSmith.Rasterizers.Gdi.GdiRasterizer();
        gdi.Capabilities.SupportedHintLevels.ShouldBe([HintLevel.Full]);
    }
#endif

#if NET10_0_WINDOWS
    [Fact]
    public void DirectWrite_AdvertisesNoneAndLight()
    {
        using var dwrite = new KernSmith.Rasterizers.DirectWrite.TerraFX.DirectWriteRasterizer();
        dwrite.Capabilities.SupportedHintLevels.ShouldBe([HintLevel.None, HintLevel.Light], ignoreOrder: true);
    }
#endif

    [Theory]
    [InlineData(HintLevel.None)]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Full)]
    public void StbTrueType_RendersEveryLevelExactlyAsDefault(HintLevel level)
    {
        // stb_truetype never hints, so every level is the unhinted output it always produced.
        var plain = BmFont.Generate(Font(), Options(RasterizerBackend.StbTrueType, HintLevel.Default));
        var levelled = BmFont.Generate(Font(), Options(RasterizerBackend.StbTrueType, level));

        levelled.FntText.ShouldBe(plain.FntText);
        levelled.Pages[0].PixelData.ShouldBe(plain.Pages[0].PixelData);
    }

    [Fact]
    public void FreeType_RendersNone_Unhinted()
    {
        // None loads FT_LOAD_NO_HINTING, exactly what EnableHinting = false does.
        var unhinted = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Default, hinting: false));
        var levelled = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.None));
        var hinted = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Default));

        levelled.FntText.ShouldBe(unhinted.FntText);
        levelled.Pages[0].PixelData.ShouldBe(unhinted.Pages[0].PixelData);
        levelled.Pages[0].PixelData.ShouldNotBe(hinted.Pages[0].PixelData, "FreeType's hinted and unhinted 12px output differ");
    }

    [Theory]
    [InlineData(HintLevel.Light)]
    [InlineData(HintLevel.Normal)]
    public void FreeType_RendersLightAndEverythingThatStepsDownToIt_LightHinted(HintLevel level)
    {
        // Light loads FT_LOAD_TARGET_LIGHT (y-only auto-hinting); Normal is not advertised and
        // steps down to Light. Neither is the unhinted output nor the default (normal) hinting.
        var unhinted = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Default, hinting: false));
        var light = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Light));
        var levelled = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, level));
        var hinted = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Default));

        levelled.FntText.ShouldBe(light.FntText);
        levelled.Pages[0].PixelData.ShouldBe(light.Pages[0].PixelData);
        levelled.Pages[0].PixelData.ShouldNotBe(unhinted.Pages[0].PixelData, "light hinting moves horizontal edges at 12px");
        levelled.Pages[0].PixelData.ShouldNotBe(hinted.Pages[0].PixelData, "normal hinting fits x too");
    }

    [Fact]
    public void FreeType_RendersFullExactlyAsDefault()
    {
        var hinted = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Default));
        var full = BmFont.Generate(Font(), Options(RasterizerBackend.FreeType, HintLevel.Full));

        full.FntText.ShouldBe(hinted.FntText);
        full.Pages[0].PixelData.ShouldBe(hinted.Pages[0].PixelData);
    }

    private static FontGeneratorOptions Options(RasterizerBackend backend, HintLevel level, bool hinting = true) => new()
    {
        Size = 12,
        Characters = CharacterSet.Ascii,
        Backend = backend,
        HintLevel = level,
        EnableHinting = hinting,
    };

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>Records the options of every request; draws each glyph as a 4x4 block.</summary>
    private sealed class RecordingRasterizer(IReadOnlyList<HintLevel> supported) : IRasterizer
    {
        public List<RasterOptions> Seen { get; } = [];

        public IRasterizerCapabilities Capabilities { get; } = new Caps(supported);

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            Seen.Add(options);
            return Block(codepoint);
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
        {
            Seen.Add(options);
            return codepoints.Select(Block).ToList();
        }

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
        {
            Seen.Add(options);
            return new GlyphMetrics(BearingX: 0, BearingY: 4, Advance: 5, Width: 4, Height: 4);
        }

        private static RasterizedGlyph Block(int codepoint) => new()
        {
            Codepoint = codepoint,
            GlyphIndex = codepoint,
            BitmapData = Enumerable.Repeat((byte)255, 16).ToArray(),
            Width = 4,
            Height = 4,
            Pitch = 4,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 4, Advance: 5, Width: 4, Height: 4),
            Format = PixelFormat.Grayscale8,
        };

        public void Dispose()
        {
        }

        private sealed class Caps(IReadOnlyList<HintLevel> supported) : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => false;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = Enum.GetValues<AntiAliasMode>();
            public IReadOnlyList<HintLevel> SupportedHintLevels { get; } = supported;
        }
    }
}
