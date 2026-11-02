using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using KernSmith;
using KernSmith.Output;
using KernSmith.Rasterizer;

// Native hinting cost per level (Phase 188): Native unhinted and Light next to FreeType hinted
// (its default, what parity targets) and FreeType unhinted, over the fonts FreeType sends to
// different engines (Roboto: auto-hinter; NotoSans-Latin: TrueType bytecode; SourceSerif4: CFF),
// Grayscale and None, at the small sizes the parity work is about plus 32px for the Phase 177 numbers.
// Per-glyph rows use OperationsPerInvoke, so Mean and Allocated read as per-glyph cost.
// Budgets live in plan/phase-188-native-hint-levels.md ("Performance plan").
// Run: dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeHinting*

/// <summary>One backend with one hinting setting on one font, as a benchmark parameter.</summary>
public sealed class NativeHintingCase
{
    /// <summary>Hinting label: <c>off</c>, <c>Light</c> (Native y-only), <c>Full</c> (FreeType default).</summary>
    public required string Hinting { get; init; }

    public required RasterizerBackend Backend { get; init; }

    /// <summary>Fixture file name inside <c>tests/KernSmith.Tests/Fixtures</c>.</summary>
    public required string Font { get; init; }

    public required AntiAliasMode AntiAlias { get; init; }

    public required bool EnableHinting { get; init; }

    // TODO (Phase 188 step 1): add a HintLevel member, set it in NativeHintingCases.All and copy it into
    // RasterOptions/FontGeneratorOptions below, with cases filtered by IRasterizerCapabilities.SupportedHintLevels.

    public RasterOptions OptionsAt(int size) => new()
    {
        Size = size,
        AntiAlias = AntiAlias,
        EnableHinting = EnableHinting,
    };

    public FontGeneratorOptions GeneratorOptionsAt(int size) => new()
    {
        Size = size,
        Characters = CharacterSet.Ascii,
        Backend = Backend,
        AntiAlias = AntiAlias,
        EnableHinting = EnableHinting,
    };

    public override string ToString() => $"{ShortFont} {AntiAlias} {Hinting} ({Backend})";

    private string ShortFont => Font switch
    {
        "Roboto-Regular.ttf" => "Roboto",
        "NotoSans-Regular-Latin.ttf" => "NotoSans",
        "SourceSerif4-Regular.otf" => "SourceSerif4",
        _ => Path.GetFileNameWithoutExtension(Font),
    };
}

internal static class NativeHintingCases
{
    public const int AsciiCount = 0x7E - 0x20 + 1;

    public static readonly int[] Ascii = CharacterSet.Ascii.GetCodepoints().ToArray();

    private static readonly string[] Fonts =
    [
        "Roboto-Regular.ttf",          // no fpgm/prep: FreeType auto-hints it
        "NotoSans-Regular-Latin.ttf",  // ttfautohint bytecode: FreeType runs the TrueType interpreter
        "SourceSerif4-Regular.otf",    // CFF: FreeType's Adobe engine
    ];

    private static readonly AntiAliasMode[] Modes = [AntiAliasMode.Grayscale, AntiAliasMode.None];

    /// <summary>
    /// Native: hinting off at the cell's mode, plus Light (grayscale only; Native's <c>None</c> is
    /// never hinted today). FreeType: hinted (<c>Full</c>, its default) and unhinted, both modes.
    /// </summary>
    public static IEnumerable<NativeHintingCase> All()
    {
        bool native = RasterizerFactory.IsRegistered(RasterizerBackend.Native);
        bool freeType = RasterizerFactory.IsRegistered(RasterizerBackend.FreeType);

        foreach (var font in Fonts)
        {
            foreach (var mode in Modes)
            {
                if (native)
                {
                    yield return new NativeHintingCase { Hinting = "off", Backend = RasterizerBackend.Native, Font = font, AntiAlias = mode, EnableHinting = false };
                    if (mode == AntiAliasMode.Grayscale)
                        yield return new NativeHintingCase { Hinting = "Light", Backend = RasterizerBackend.Native, Font = font, AntiAlias = AntiAliasMode.Light, EnableHinting = true };
                }

                if (freeType)
                {
                    yield return new NativeHintingCase { Hinting = "Full", Backend = RasterizerBackend.FreeType, Font = font, AntiAlias = mode, EnableHinting = true };
                    yield return new NativeHintingCase { Hinting = "off", Backend = RasterizerBackend.FreeType, Font = font, AntiAlias = mode, EnableHinting = false };
                }
            }
        }
    }
}

[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeHinting")]
public class NativeHintingRasterizeBenchmarks
{
    private IRasterizer _rasterizer = null!;
    private RasterOptions _options = null!;

    [ParamsSource(nameof(Cases))]
    public NativeHintingCase Case { get; set; } = null!;

    public static IEnumerable<NativeHintingCase> Cases() => NativeHintingCases.All();

    [Params(8, 12, 16, 32)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _options = Case.OptionsAt(Size);
        _rasterizer = RasterizerFactory.Create(Case.Backend);
        _rasterizer.LoadFont(BenchmarkFixtures.ReadFont(Case.Font));
    }

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "RasterizeAll ASCII (per glyph)", OperationsPerInvoke = NativeHintingCases.AsciiCount)]
    public IReadOnlyList<RasterizedGlyph> RasterizeAscii() => _rasterizer.RasterizeAll(NativeHintingCases.Ascii, _options);

    [Benchmark(Description = "GetGlyphMetrics ASCII (per glyph)", OperationsPerInvoke = NativeHintingCases.AsciiCount)]
    public int GetMetricsAscii() => NativeFeatureMeasure.SumAdvances(_rasterizer, NativeHintingCases.Ascii, _options);
}

/// <summary>The full public path per hinting setting: what a caller pays for a small ASCII font.</summary>
[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeHinting")]
public class NativeHintingEndToEndBenchmarks
{
    private byte[] _fontData = null!;
    private FontGeneratorOptions _options = null!;

    [ParamsSource(nameof(Cases))]
    public NativeHintingCase Case { get; set; } = null!;

    public static IEnumerable<NativeHintingCase> Cases() => NativeHintingCases.All();

    [Params(12, 32)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _fontData = BenchmarkFixtures.ReadFont(Case.Font);
        _options = Case.GeneratorOptionsAt(Size);
    }

    [Benchmark(Description = "BmFont.Generate ASCII")]
    public BmFontResult Generate() => BmFont.Generate(_fontData, _options);
}
