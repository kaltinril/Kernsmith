using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using KernSmith;
using KernSmith.Output;
using KernSmith.Rasterizer;

// Native feature paths (Phase 177): one row per feature x backend, so a slow Native path shows up
// next to the plain Grayscale baseline and next to FreeType/StbTrueType doing the same work.
// A backend appears in a case only when its capabilities advertise the feature; unsupported
// combinations are never generated, so no case throws.
// Run: dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeFeature*

/// <summary>One backend rendering one font with one set of options, as a benchmark parameter.</summary>
public sealed class NativeFeatureCase
{
    /// <summary>Short feature label shown in the summary table.</summary>
    public required string Feature { get; init; }

    /// <summary>The backend under test.</summary>
    public required RasterizerBackend Backend { get; init; }

    /// <summary>Fixture file name inside <c>tests/KernSmith.Tests/Fixtures</c>.</summary>
    public required string Font { get; init; }

    /// <summary>Codepoints passed to <see cref="IRasterizer.RasterizeAll"/>.</summary>
    public required int[] Codepoints { get; init; }

    /// <summary>Options for every call; <see cref="RasterOptions.VariationAxes"/> also selects the instance.</summary>
    public required RasterOptions Options { get; init; }

    /// <summary>Creates the backend, loads the font and selects the variable instance, as core does.</summary>
    public IRasterizer CreateRasterizer()
    {
        var fontData = BenchmarkFixtures.ReadFont(Font);
        var rasterizer = RasterizerFactory.Create(Backend);
        rasterizer.LoadFont(fontData);

        if (Options.VariationAxes is { Count: > 0 } axes
            && BmFont.ReadFontInfo(fontData).VariationAxes is { Count: > 0 } fvarAxes)
        {
            rasterizer.SetVariationAxes(fvarAxes, axes);
        }

        return rasterizer;
    }

    public override string ToString() => $"{Feature} ({Backend})";
}

/// <summary>Builds the <see cref="NativeFeatureCase"/> lists shared by the feature classes.</summary>
internal static class NativeFeatureCases
{
    public const int AsciiCount = 0x7E - 0x20 + 1;
    public const int ColorGlyphCount = 10;

    // Roboto's composite glyphs in U+00C0-U+00FF: every Latin-1 letter except the 11 drawn as
    // simple glyphs (Æ Ð × Ø Þ ß æ ð ÷ ø þ). Keep CompositeCount in sync with the list.
    public const int CompositeCount = 53;

    private static readonly int[] Ascii = CharacterSet.Ascii.GetCodepoints().ToArray();

    private static readonly int[] NonCompositeLatin1 =
        [0xC6, 0xD0, 0xD7, 0xD8, 0xDE, 0xDF, 0xE6, 0xF0, 0xF7, 0xF8, 0xFE];

    public static int[] Latin1Composites { get; } =
        Enumerable.Range(0xC0, 0x40).Except(NonCompositeLatin1).ToArray();

    private static readonly int[] ArabicIndicDigits = Enumerable.Range(0x0660, ColorGlyphCount).ToArray();

    private static readonly int[] Emoji =
        [0x1F600, 0x1F601, 0x1F602, 0x1F603, 0x1F604, 0x1F60D, 0x1F44D, 0x2764, 0x1F525, 0x1F680];

    private sealed record Definition(
        string Feature,
        string Font,
        int[] Codepoints,
        RasterOptions Options,
        Func<RasterizerBackend, IRasterizerCapabilities, bool> Supported);

    private static RasterOptions Px32 => new() { Size = 32 };

    private static readonly Definition[] AsciiDefinitions =
    [
        new("Grayscale", "Roboto-Regular.ttf", Ascii, Px32, (_, _) => true),
        new("Sdf 12px", "Roboto-Regular.ttf", Ascii, SdfAt(12), (_, c) => c.SupportsSdf),
        new("Sdf 32px", "Roboto-Regular.ttf", Ascii, SdfAt(32), (_, c) => c.SupportsSdf),
        new("Sdf 72px", "Roboto-Regular.ttf", Ascii, SdfAt(72), (_, c) => c.SupportsSdf),
        new("Msdf", "Roboto-Regular.ttf", Ascii, Px32 with { SdfMode = SdfMode.Msdf }, (_, c) => c.SupportsMsdf),
        new("Mtsdf", "Roboto-Regular.ttf", Ascii, Px32 with { SdfMode = SdfMode.Mtsdf }, (_, c) => c.SupportsMsdf),
        new("Lcd", "Roboto-Regular.ttf", Ascii, Px32 with { AntiAlias = AntiAliasMode.Lcd },
            (_, c) => c.SupportedAntiAliasModes.Contains(AntiAliasMode.Lcd)),
        new("Light hinted", "Roboto-Regular.ttf", Ascii, Px32 with { AntiAlias = AntiAliasMode.Light, EnableHinting = true },
            (_, c) => c.SupportedAntiAliasModes.Contains(AntiAliasMode.Light)),
        new("Bold", "Roboto-Regular.ttf", Ascii, Px32 with { Bold = true }, (_, c) => c.SupportsSyntheticBold),
        new("Italic", "Roboto-Regular.ttf", Ascii, Px32 with { Italic = true }, (_, c) => c.SupportsSyntheticItalic),
        new("Bold+Italic", "Roboto-Regular.ttf", Ascii, Px32 with { Bold = true, Italic = true },
            (_, c) => c.SupportsSyntheticBold && c.SupportsSyntheticItalic),
        new("Width 150%", "Roboto-Regular.ttf", Ascii, Px32 with { WidthPercent = 150 }, (_, c) => c.SupportsWidthScale),
        new("Italic 30deg", "Roboto-Regular.ttf", Ascii, Px32 with { Italic = true, ItalicAngle = 30f },
            (_, c) => c.SupportsSyntheticItalic && c.SupportsItalicAngle),
        new("CFF", "SourceSerif4-Regular.otf", Ascii, Px32, (_, _) => true),
        new("CFF2 wght 900", "SourceCodeVF-Upright.otf", Ascii, Wght900, (_, c) => c.SupportsVariableFonts),
        new("gvar wght 900", "RobotoFlex-Variable.ttf", Ascii, Wght900, (_, c) => c.SupportsVariableFonts),
        new("COLR v0", Path.Combine("Color", "BungeeColor-Regular.ttf"), Ascii, Px32 with { ColorFont = true },
            (_, c) => c.SupportsColorFonts),
    ];

    private static readonly Definition[] ColorGlyphDefinitions =
    [
        // FreeType draws COLR v1 glyphs as plain grayscale coverage, so only Native does the work.
        new("COLR v1", Path.Combine("Color", "ReemKufiInk-Regular.ttf"), ArabicIndicDigits, Px32 with { ColorFont = true },
            (b, c) => b == RasterizerBackend.Native && c.SupportsColorFonts),
        // FreeType returns the strike's own size (no scaling); Native scales the strike to 32px.
        new("CBDT emoji", "NotoColorEmoji.ttf", Emoji, Px32 with { ColorFont = true }, (_, c) => c.SupportsColorFonts),
    ];

    /// <summary>Printable-ASCII cases (<see cref="AsciiCount"/> glyphs each).</summary>
    public static IEnumerable<NativeFeatureCase> AsciiCases() => Expand(AsciiDefinitions);

    /// <summary>Color-glyph cases (<see cref="ColorGlyphCount"/> glyphs each).</summary>
    public static IEnumerable<NativeFeatureCase> ColorGlyphCases() => Expand(ColorGlyphDefinitions);

    private static RasterOptions SdfAt(int size) => new() { Size = size, Sdf = true, SdfMode = SdfMode.Sdf, SdfSpread = 8f };

    private static RasterOptions Wght900 => Px32 with { VariationAxes = new Dictionary<string, float> { ["wght"] = 900f } };

    private static IEnumerable<NativeFeatureCase> Expand(IEnumerable<Definition> definitions)
    {
        var capabilities = BenchmarkBackends.Portable.ToDictionary(b => b, b =>
        {
            using var rasterizer = RasterizerFactory.Create(b);
            return rasterizer.Capabilities;
        });

        foreach (var definition in definitions)
        {
            foreach (var (backend, caps) in capabilities)
            {
                if (!definition.Supported(backend, caps))
                    continue;

                yield return new NativeFeatureCase
                {
                    Feature = definition.Feature,
                    Backend = backend,
                    Font = definition.Font,
                    Codepoints = definition.Codepoints,
                    Options = definition.Options,
                };
            }
        }
    }
}

/// <summary>Widens the parameter column so "feature (backend)" labels are not truncated.</summary>
public sealed class NativeFeatureConfig : ManualConfig
{
    public NativeFeatureConfig() => SummaryStyle = SummaryStyle.Default.WithMaxParameterColumnWidth(40);
}

[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeFeature")]
public class NativeFeatureRasterizeBenchmarks
{
    private IRasterizer _rasterizer = null!;

    [ParamsSource(nameof(Cases))]
    public NativeFeatureCase Case { get; set; } = null!;

    public static IEnumerable<NativeFeatureCase> Cases() => NativeFeatureCases.AsciiCases();

    [GlobalSetup]
    public void Setup() => _rasterizer = Case.CreateRasterizer();

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "RasterizeAll ASCII (per glyph)", OperationsPerInvoke = NativeFeatureCases.AsciiCount)]
    public IReadOnlyList<RasterizedGlyph> RasterizeAscii() => _rasterizer.RasterizeAll(Case.Codepoints, Case.Options);
}

[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeFeature")]
public class NativeFeatureMetricsBenchmarks
{
    private IRasterizer _rasterizer = null!;

    [ParamsSource(nameof(Cases))]
    public NativeFeatureCase Case { get; set; } = null!;

    public static IEnumerable<NativeFeatureCase> Cases() => NativeFeatureCases.AsciiCases();

    [GlobalSetup]
    public void Setup() => _rasterizer = Case.CreateRasterizer();

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "GetGlyphMetrics ASCII (per glyph)", OperationsPerInvoke = NativeFeatureCases.AsciiCount)]
    public int GetMetricsAscii() => NativeFeatureMeasure.SumAdvances(_rasterizer, Case.Codepoints, Case.Options);
}

[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeFeature")]
public class NativeFeatureColorGlyphBenchmarks
{
    private IRasterizer _rasterizer = null!;

    [ParamsSource(nameof(Cases))]
    public NativeFeatureCase Case { get; set; } = null!;

    public static IEnumerable<NativeFeatureCase> Cases() => NativeFeatureCases.ColorGlyphCases();

    [GlobalSetup]
    public void Setup() => _rasterizer = Case.CreateRasterizer();

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "RasterizeAll color (per glyph)", OperationsPerInvoke = NativeFeatureCases.ColorGlyphCount)]
    public IReadOnlyList<RasterizedGlyph> RasterizeColor() => _rasterizer.RasterizeAll(Case.Codepoints, Case.Options);

    [Benchmark(Description = "GetGlyphMetrics color (per glyph)", OperationsPerInvoke = NativeFeatureCases.ColorGlyphCount)]
    public int GetMetricsColor() => NativeFeatureMeasure.SumAdvances(_rasterizer, Case.Codepoints, Case.Options);
}

[MemoryDiagnoser]
[BenchmarkCategory("NativeFeature")]
public class NativeFeatureCompositeBenchmarks
{
    private IRasterizer _rasterizer = null!;
    private int[] _codepoints = null!;
    private RasterOptions _options = null!;

    [ParamsSource(nameof(Backends))]
    public RasterizerBackend Backend { get; set; }

    public static IEnumerable<RasterizerBackend> Backends => BenchmarkBackends.Portable;

    [GlobalSetup]
    public void Setup()
    {
        _codepoints = NativeFeatureCases.Latin1Composites;
        if (_codepoints.Length != NativeFeatureCases.CompositeCount)
            throw new InvalidOperationException(
                $"Expected {NativeFeatureCases.CompositeCount} composite codepoints, got {_codepoints.Length}.");

        _options = new RasterOptions { Size = 32 };
        _rasterizer = RasterizerFactory.Create(Backend);
        _rasterizer.LoadFont(BenchmarkFixtures.ReadFont("Roboto-Regular.ttf"));
    }

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "RasterizeAll Latin-1 composites 32px (per glyph)", OperationsPerInvoke = NativeFeatureCases.CompositeCount)]
    public IReadOnlyList<RasterizedGlyph> RasterizeComposites() => _rasterizer.RasterizeAll(_codepoints, _options);

    [Benchmark(Description = "GetGlyphMetrics Latin-1 composites 32px (per glyph)", OperationsPerInvoke = NativeFeatureCases.CompositeCount)]
    public int GetMetricsComposites() => NativeFeatureMeasure.SumAdvances(_rasterizer, _codepoints, _options);
}

/// <summary>
/// Pipeline-level features: <see cref="OutlineMode"/> is a <see cref="FontGeneratorOptions"/>
/// setting that core turns into <see cref="IRasterizer.RasterizeOutlineLayer"/> calls, so the
/// vector outline is measured through <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/>.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(NativeFeatureConfig))]
[BenchmarkCategory("NativeFeature")]
public class NativeFeatureEndToEndBenchmarks
{
    private byte[] _fontData = null!;

    public sealed record GenerateCase(string Feature, RasterizerBackend Backend, Action<FontGeneratorOptions> Apply)
    {
        public override string ToString() => $"{Feature} ({Backend})";
    }

    [ParamsSource(nameof(Cases))]
    public GenerateCase Case { get; set; } = null!;

    public static IEnumerable<GenerateCase> Cases()
    {
        foreach (var backend in BenchmarkBackends.Portable)
        {
            using var rasterizer = RasterizerFactory.Create(backend);
            var caps = rasterizer.Capabilities;

            yield return new GenerateCase("Outline 2 effect", backend, o => o.Outline = 2);
            if (caps.SupportsOutlineStroke)
                yield return new GenerateCase("Outline 2 vector", backend, o => { o.Outline = 2; o.OutlineMode = OutlineMode.Vector; });
            if (caps.SupportsSdf)
                yield return new GenerateCase("Sdf", backend, o => o.Sdf = true);
            if (caps.SupportsMsdf)
                yield return new GenerateCase("Msdf", backend, o => o.SdfMode = SdfMode.Msdf);
        }
    }

    [GlobalSetup]
    public void Setup() => _fontData = BenchmarkFixtures.ReadFont("Roboto-Regular.ttf");

    [Benchmark(Description = "BmFont.Generate ASCII 32px")]
    public BmFontResult Generate()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = Case.Backend
        };
        Case.Apply(options);
        return BmFont.Generate(_fontData, options);
    }
}

internal static class NativeFeatureMeasure
{
    public static int SumAdvances(IRasterizer rasterizer, int[] codepoints, RasterOptions options)
    {
        int sum = 0;
        foreach (int codepoint in codepoints)
        {
            var metrics = rasterizer.GetGlyphMetrics(codepoint, options);
            if (metrics is not null)
                sum += metrics.Value.Advance;
        }

        return sum;
    }
}
