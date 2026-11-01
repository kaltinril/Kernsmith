using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 173 — the public LCD options in core: defaults, <see cref="RasterOptions"/> mapping, the
/// builder, <c>.bmfc</c> keys, and the rule that LCD output is never super sampled.
/// </summary>
[Collection("RasterizerFactory")]
public class LcdOptionsTests
{
    private static readonly string FontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] Font() => File.ReadAllBytes(FontPath);

    private static readonly AntiAliasMode[] AllModes = Enum.GetValues<AntiAliasMode>();
    private static readonly AntiAliasMode[] NoneAndGrayscale = [AntiAliasMode.None, AntiAliasMode.Grayscale];

    // ---- Options ------------------------------------------------------------------------------

    [Fact]
    public void Defaults_AreTheBackendsFilterAndRgb()
    {
        var options = new FontGeneratorOptions();
        options.LcdFilter.ShouldBe(LcdFilter.Default);
        options.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.Rgb);

        var raster = new RasterOptions { Size = 12 };
        raster.LcdFilter.ShouldBe(LcdFilter.Default);
        raster.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.Rgb);
    }

    [Theory]
    [InlineData(LcdFilter.Smooth, LcdSubpixelOrder.Bgr)]
    [InlineData(LcdFilter.None, LcdSubpixelOrder.VRgb)]
    [InlineData(LcdFilter.Light, LcdSubpixelOrder.VBgr)]
    public void FromGeneratorOptions_CarriesBothOptions(LcdFilter filter, LcdSubpixelOrder order)
    {
        var raster = RasterOptions.FromGeneratorOptions(new FontGeneratorOptions { LcdFilter = filter, LcdSubpixelOrder = order });

        raster.LcdFilter.ShouldBe(filter);
        raster.LcdSubpixelOrder.ShouldBe(order);
    }

    [Fact]
    public void Builder_WithLcdOptions_AndFromConfig_ReachTheRasterizer()
    {
        var direct = new RecordingRasterizer(AllModes);
        BmFont.Builder().WithFont(Font()).WithSize(24).WithCharacters(CharacterSet.FromChars("A"))
            .WithRasterizer(direct).WithAntiAlias(AntiAliasMode.Lcd)
            .WithLcdFilter(LcdFilter.Smooth).WithLcdSubpixelOrder(LcdSubpixelOrder.VBgr).Build();

        direct.Seen.ShouldNotBeEmpty();
        direct.Seen.ShouldAllBe(o => o.LcdFilter == LcdFilter.Smooth && o.LcdSubpixelOrder == LcdSubpixelOrder.VBgr);

        var config = BmfcConfigReader.Parse(
            $"fontFile={FontPath}\nfontSize=24\nchars=65\nantiAlias=lcd\nlcdFilter=none\nlcdOrder=bgr\n");
        var fromConfig = new RecordingRasterizer(AllModes);
        BmFont.Builder().FromConfig(config).WithRasterizer(fromConfig).Build();

        fromConfig.Seen.ShouldNotBeEmpty();
        fromConfig.Seen.ShouldAllBe(o => o.LcdFilter == LcdFilter.None && o.LcdSubpixelOrder == LcdSubpixelOrder.Bgr);
    }

    // ---- .bmfc --------------------------------------------------------------------------------

    [Theory]
    [InlineData("light", LcdFilter.Light)]
    [InlineData("smooth", LcdFilter.Smooth)]
    [InlineData("none", LcdFilter.None)]
    [InlineData("SMOOTH", LcdFilter.Smooth)]
    public void Bmfc_LcdFilterKey_IsReadAndRoundTrips(string value, LcdFilter expected)
    {
        var parsed = BmfcConfigReader.Parse($"fontSize=32\nantiAlias=lcd\nlcdFilter={value}\n");
        parsed.Options.LcdFilter.ShouldBe(expected);

        var written = BmfcConfigWriter.Write(parsed);
        written.ShouldContain($"lcdFilter={expected.ToString().ToLowerInvariant()}", Case.Sensitive);
        BmfcConfigReader.Parse(written).Options.LcdFilter.ShouldBe(expected);
    }

    [Theory]
    [InlineData("bgr", LcdSubpixelOrder.Bgr)]
    [InlineData("vrgb", LcdSubpixelOrder.VRgb)]
    [InlineData("vbgr", LcdSubpixelOrder.VBgr)]
    [InlineData("VRGB", LcdSubpixelOrder.VRgb)]
    public void Bmfc_LcdOrderKey_IsReadAndRoundTrips(string value, LcdSubpixelOrder expected)
    {
        var parsed = BmfcConfigReader.Parse($"fontSize=32\nantiAlias=lcd\nlcdOrder={value}\n");
        parsed.Options.LcdSubpixelOrder.ShouldBe(expected);

        var written = BmfcConfigWriter.Write(parsed);
        written.ShouldContain($"lcdOrder={expected.ToString().ToLowerInvariant()}", Case.Sensitive);
        BmfcConfigReader.Parse(written).Options.LcdSubpixelOrder.ShouldBe(expected);
    }

    [Fact]
    public void Bmfc_DefaultsWriteNoLcdKeys_AndUnknownValuesKeepTheDefaults()
    {
        var written = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions { AntiAlias = AntiAliasMode.Lcd }));
        written.ShouldNotContain("lcdFilter", Case.Sensitive);
        written.ShouldNotContain("lcdOrder", Case.Sensitive);

        var parsed = BmfcConfigReader.Parse("fontSize=32\nlcdFilter=bogus\nlcdOrder=42\n").Options;
        parsed.LcdFilter.ShouldBe(LcdFilter.Default);
        parsed.LcdSubpixelOrder.ShouldBe(LcdSubpixelOrder.Rgb);
    }

    // ---- Super sampling -----------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void SuperSampling_IsSkippedForLcd_SoTheBackendRendersAtTheFinalSize(int level)
    {
        var plain = Recorded(AllModes, AntiAliasMode.Lcd, superSample: 1);
        var supersampled = Recorded(AllModes, AntiAliasMode.Lcd, superSample: level);

        supersampled.AntiAlias.ShouldBe(AntiAliasMode.Lcd);
        supersampled.Size.ShouldBe(plain.Size, "LCD renders at the final size");
        supersampled.SuperSample.ShouldBe(1, "a backend that supersamples itself must not either");
    }

    [Fact]
    public void SuperSampling_StillAppliesToOtherModes()
    {
        var plain = Recorded(AllModes, AntiAliasMode.Grayscale, superSample: 1);
        var supersampled = Recorded(AllModes, AntiAliasMode.Grayscale, superSample: 2);

        supersampled.Size.ShouldBe(plain.Size * 2);
        supersampled.SuperSample.ShouldBe(1, "core does the super sampling; the backend renders once at Size x level");
    }

    [Fact]
    public void SuperSampling_StillApplies_WhenTheBackendDegradesLcdToGrayscale()
    {
        // The rule follows the effective mode: a backend without LCD renders grayscale, which
        // super samples as usual — exactly what an explicit Grayscale request gets.
        var degraded = Recorded(NoneAndGrayscale, AntiAliasMode.Lcd, superSample: 2);
        var grayscale = Recorded(NoneAndGrayscale, AntiAliasMode.Grayscale, superSample: 2);

        degraded.AntiAlias.ShouldBe(AntiAliasMode.Grayscale);
        degraded.Size.ShouldBe(grayscale.Size);
        degraded.Size.ShouldBe(Recorded(NoneAndGrayscale, AntiAliasMode.Grayscale, superSample: 1).Size * 2);
        degraded.SuperSample.ShouldBe(1);
    }

    [Theory]
    [InlineData(RasterizerBackend.StbTrueType)]
    [InlineData(RasterizerBackend.FreeType)]
    public void LcdOptions_DoNotChangeOtherBackendsOutput(RasterizerBackend backend)
    {
        // Only Native honours the LCD options; StbTrueType degrades Lcd to grayscale and the
        // FreeType backend renders its own LCD without them.
        FontGeneratorOptions Options(LcdFilter filter, LcdSubpixelOrder order) => new()
        {
            Size = 20,
            Characters = CharacterSet.FromChars("AVWaeg0@"),
            Backend = backend,
            AntiAlias = AntiAliasMode.Lcd,
            LcdFilter = filter,
            LcdSubpixelOrder = order,
        };

        var plain = BmFont.Generate(Font(), Options(LcdFilter.Default, LcdSubpixelOrder.Rgb));
        var tuned = BmFont.Generate(Font(), Options(LcdFilter.Smooth, LcdSubpixelOrder.VBgr));

        tuned.FntText.ShouldBe(plain.FntText);
        tuned.Pages[0].PixelData.ShouldBe(plain.Pages[0].PixelData);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>The options the backend received for one generation.</summary>
    private static RasterOptions Recorded(AntiAliasMode[] supported, AntiAliasMode mode, int superSample)
    {
        var recording = new RecordingRasterizer(supported);
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Rasterizer = recording,
            AntiAlias = mode,
            SuperSampleLevel = superSample,
        };

        using (var prepared = PreparedRasterization.Prepare(Font(), options, systemFontName: null, subsetToCharacterSet: true))
        {
            prepared.RasterizeAndProcess(new[] { (int)'A' }, equalizeTargetHeight: null);
        }

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.Distinct().Count().ShouldBe(1);
        return recording.Seen[0];
    }

    /// <summary>Records the options of every render request; draws each glyph as a 4x4 block.</summary>
    private sealed class RecordingRasterizer(IReadOnlyList<AntiAliasMode> supported) : IRasterizer
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

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) =>
            new GlyphMetrics(BearingX: 0, BearingY: 4, Advance: 5, Width: 4, Height: 4);

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

        private sealed class Caps(IReadOnlyList<AntiAliasMode> supported) : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => false;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = supported;
        }
    }
}
