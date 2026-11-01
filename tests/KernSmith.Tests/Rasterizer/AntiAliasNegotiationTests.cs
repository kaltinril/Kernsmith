using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 173 — core resolves the requested anti-alias mode against the backend's
/// <see cref="IRasterizerCapabilities.SupportedAntiAliasModes"/> before any rasterizer call.
/// </summary>
[Collection("RasterizerFactory")]
public class AntiAliasNegotiationTests
{
    private static readonly AntiAliasMode[] NoneAndGrayscale = [AntiAliasMode.None, AntiAliasMode.Grayscale];

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---- The policy -----------------------------------------------------------------------------

    [Theory]
    [InlineData(AntiAliasMode.None, AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale, AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light, AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Lcd, AntiAliasMode.Grayscale)]
    public void UnsupportedModes_DegradeToGrayscale(AntiAliasMode requested, AntiAliasMode expected)
    {
        AntiAliasNegotiation.Resolve(requested, NoneAndGrayscale).ShouldBe(expected);
    }

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void SupportedModes_PassThrough(AntiAliasMode requested)
    {
        AntiAliasNegotiation.Resolve(requested, Enum.GetValues<AntiAliasMode>()).ShouldBe(requested);
    }

    [Fact]
    public void Lcd_PassesThrough_WhenOnlyLightIsMissing()
    {
        AntiAliasMode[] supported = [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Lcd];
        AntiAliasNegotiation.Resolve(AntiAliasMode.Lcd, supported).ShouldBe(AntiAliasMode.Lcd);
        AntiAliasNegotiation.Resolve(AntiAliasMode.Light, supported).ShouldBe(AntiAliasMode.Grayscale);
    }

    [Fact]
    public void None_IsAlwaysHonored_EvenWhenNotAdvertised()
    {
        AntiAliasNegotiation.Resolve(AntiAliasMode.None, [AntiAliasMode.Grayscale]).ShouldBe(AntiAliasMode.None);
    }

    [Fact]
    public void ABackendAdvertisingNoUsableFallback_GetsTheRequestUnchanged()
    {
        // Nothing to degrade to: leave the request alone rather than invent a mode the backend
        // never named — what every backend received before negotiation existed.
        AntiAliasNegotiation.Resolve(AntiAliasMode.Lcd, []).ShouldBe(AntiAliasMode.Lcd);
        AntiAliasNegotiation.Resolve(AntiAliasMode.Light, [AntiAliasMode.Lcd]).ShouldBe(AntiAliasMode.Light);
    }

    [Fact]
    public void Degrading_NeverCrossesTheSmoothingBoundary()
    {
        // .fnt `smooth` and the outline effect's smoothing both test `AntiAlias != None`. Because
        // negotiation never turns a smooth mode into None (or None into a smooth one), they
        // describe the rendered pixels whatever the backend does.
        var supportSets = new[]
        {
            Array.Empty<AntiAliasMode>(),
            [AntiAliasMode.Grayscale],
            NoneAndGrayscale,
            [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Lcd],
            Enum.GetValues<AntiAliasMode>(),
        };

        foreach (var supported in supportSets)
        {
            foreach (var requested in Enum.GetValues<AntiAliasMode>())
            {
                var effective = AntiAliasNegotiation.Resolve(requested, supported);
                (effective == AntiAliasMode.None).ShouldBe(requested == AntiAliasMode.None,
                    $"{requested} against [{string.Join(", ", supported)}] became {effective}");
            }
        }
    }

    // ---- Where core applies it ----------------------------------------------------------------

    [Theory]
    [InlineData(AntiAliasMode.Lcd, AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light, AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.None, AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale, AntiAliasMode.Grayscale)]
    public void Generation_HandsTheBackendTheEffectiveMode(AntiAliasMode requested, AntiAliasMode expected)
    {
        var recording = new RecordingRasterizer(NoneAndGrayscale);
        var options = new FontGeneratorOptions { Size = 32, Rasterizer = recording, AntiAlias = requested };

        using (var prepared = PreparedRasterization.Prepare(
            LoadTestFont(), options, systemFontName: null, subsetToCharacterSet: true))
        {
            prepared.RasterizeAndProcess(new[] { (int)'A' }, equalizeSpan: null);
        }

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(mode => mode == expected);
        options.AntiAlias.ShouldBe(requested, "the caller's options are not rewritten");
    }

    [Fact]
    public void Generation_PassesASupportedLcdThrough()
    {
        var recording = new RecordingRasterizer(Enum.GetValues<AntiAliasMode>());
        var options = new FontGeneratorOptions { Size = 32, Rasterizer = recording, AntiAlias = AntiAliasMode.Lcd };

        using (var prepared = PreparedRasterization.Prepare(
            LoadTestFont(), options, systemFontName: null, subsetToCharacterSet: true))
        {
            prepared.RasterizeAndProcess(new[] { (int)'A' }, equalizeSpan: null);
        }

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(mode => mode == AntiAliasMode.Lcd);
    }

    [Fact]
    public void AtlasSizeQuery_MeasuresWithTheEffectiveMode()
    {
        var recording = new RecordingRasterizer(NoneAndGrayscale);
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("AB"),
            Rasterizer = recording,
            AntiAlias = AntiAliasMode.Lcd,
        };

        BmFont.QueryAtlasSize(LoadTestFont(), options);

        recording.Seen.ShouldNotBeEmpty();
        recording.Seen.ShouldAllBe(mode => mode == AntiAliasMode.Grayscale);
    }

    // ---- Existing backends are unaffected ---------------------------------------------------------

    [Theory]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void StbTrueType_RendersUnadvertisedModesExactlyAsGrayscale(AntiAliasMode requested)
    {
        // StbTrueType advertises None and Grayscale and always rendered anything else as
        // grayscale; negotiation makes that explicit without moving a pixel or a metadata flag.
        var font = LoadTestFont();
        static FontGeneratorOptions Options(AntiAliasMode mode) => new()
        {
            Size = 24,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.StbTrueType,
            AntiAlias = mode,
        };

        var degraded = BmFont.Generate(font, Options(requested));
        var grayscale = BmFont.Generate(font, Options(AntiAliasMode.Grayscale));

        degraded.FntText.ShouldBe(grayscale.FntText);
        degraded.Pages[0].PixelData.ShouldBe(grayscale.Pages[0].PixelData);
        degraded.FntText.ShouldContain("smooth=1", Case.Sensitive);
    }

    /// <summary>Records the anti-alias mode of every request it is handed; draws nothing.</summary>
    private sealed class RecordingRasterizer(IReadOnlyList<AntiAliasMode> supported) : IRasterizer
    {
        public List<AntiAliasMode> Seen { get; } = [];

        public IRasterizerCapabilities Capabilities { get; } = new Caps(supported);

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            Seen.Add(options.AntiAlias);
            return null;
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
        {
            Seen.Add(options.AntiAlias);
            return Array.Empty<RasterizedGlyph>();
        }

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
        {
            Seen.Add(options.AntiAlias);
            return new GlyphMetrics(BearingX: 0, BearingY: 10, Advance: 12, Width: 10, Height: 10);
        }

        public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
        {
            Seen.Add(options.AntiAlias);
            return new RasterizerFontMetrics { Ascent = 20, Descent = 6, LineHeight = 26 };
        }

        public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
        {
            Seen.Add(options.AntiAlias);
            return Array.Empty<ScaledKerningPair>();
        }

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
