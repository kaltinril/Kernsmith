using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;
using KernSmith.Rasterizer;

// Native vs StbTrueType vs FreeType on the same font and options (Phase 177 harness).
// Rasterizer-only classes call the IRasterizer directly so pipeline cost does not mask
// backend cost; NativeComparisonEndToEndBenchmarks shows the full BmFont.Generate cost.
// Run: dotnet run -c Release -f net10.0 --project benchmarks/KernSmith.Benchmarks -- --filter *NativeComparison*

[MemoryDiagnoser]
[BenchmarkCategory("NativeComparison")]
public class NativeComparisonLoadFontBenchmarks
{
    private byte[] _fontData = null!;

    [Params(RasterizerBackend.Native, RasterizerBackend.StbTrueType, RasterizerBackend.FreeType)]
    public RasterizerBackend Backend { get; set; }

    [GlobalSetup]
    public void Setup() => _fontData = NativeComparisonFixtures.LoadRoboto();

    // Create + LoadFont + Dispose: what a fresh BmFont.Generate pays before the first glyph.
    [Benchmark(Description = "LoadFont")]
    public IRasterizerCapabilities LoadFont()
    {
        using var rasterizer = RasterizerFactory.Create(Backend);
        rasterizer.LoadFont(_fontData);
        return rasterizer.Capabilities;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("NativeComparison")]
public class NativeComparisonRasterizeBenchmarks
{
    // OperationsPerInvoke = ASCII glyph count, so Mean and Allocated read as per-glyph cost.
    private const int AsciiCount = 0x7E - 0x20 + 1;

    private IRasterizer _rasterizer = null!;
    private int[] _codepoints = null!;
    private RasterOptions _options = null!;

    [Params(RasterizerBackend.Native, RasterizerBackend.StbTrueType, RasterizerBackend.FreeType)]
    public RasterizerBackend Backend { get; set; }

    [Params(12, 32, 72)]
    public int Size { get; set; }

    [Params(AntiAliasMode.Grayscale, AntiAliasMode.None)]
    public AntiAliasMode AntiAlias { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _codepoints = CharacterSet.Ascii.GetCodepoints().ToArray();
        if (_codepoints.Length != AsciiCount)
            throw new InvalidOperationException($"Expected {AsciiCount} ASCII codepoints, got {_codepoints.Length}.");

        _options = new RasterOptions { Size = Size, AntiAlias = AntiAlias };
        _rasterizer = RasterizerFactory.Create(Backend);
        _rasterizer.LoadFont(NativeComparisonFixtures.LoadRoboto());
    }

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "RasterizeAll ASCII (per glyph)", OperationsPerInvoke = AsciiCount)]
    public IReadOnlyList<RasterizedGlyph> RasterizeAscii() => _rasterizer.RasterizeAll(_codepoints, _options);
}

[MemoryDiagnoser]
[BenchmarkCategory("NativeComparison")]
public class NativeComparisonMetricsBenchmarks
{
    private const int AsciiCount = 0x7E - 0x20 + 1;

    private IRasterizer _rasterizer = null!;
    private int[] _codepoints = null!;
    private RasterOptions _options = null!;

    [Params(RasterizerBackend.Native, RasterizerBackend.StbTrueType, RasterizerBackend.FreeType)]
    public RasterizerBackend Backend { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _codepoints = CharacterSet.Ascii.GetCodepoints().ToArray();
        _options = new RasterOptions { Size = 32 };
        _rasterizer = RasterizerFactory.Create(Backend);
        _rasterizer.LoadFont(NativeComparisonFixtures.LoadRoboto());
    }

    [GlobalCleanup]
    public void Cleanup() => _rasterizer.Dispose();

    [Benchmark(Description = "GetGlyphMetrics ASCII 32px (per glyph)", OperationsPerInvoke = AsciiCount)]
    public int GetMetricsAscii()
    {
        int sum = 0;
        foreach (int codepoint in _codepoints)
        {
            var metrics = _rasterizer.GetGlyphMetrics(codepoint, _options);
            if (metrics is not null)
                sum += metrics.Value.Advance;
        }

        return sum;
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("NativeComparison")]
public class NativeComparisonEndToEndBenchmarks
{
    private byte[] _fontData = null!;

    [Params(RasterizerBackend.Native, RasterizerBackend.StbTrueType, RasterizerBackend.FreeType)]
    public RasterizerBackend Backend { get; set; }

    [GlobalSetup]
    public void Setup() => _fontData = NativeComparisonFixtures.LoadRoboto();

    [Benchmark(Description = "BmFont.Generate ASCII+Latin-1 32px")]
    public BmFontResult Generate() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii,
        Backend = Backend
    });
}

internal static class NativeComparisonFixtures
{
    public static byte[] LoadRoboto() => File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));

    // BenchmarkDotNet runs each case in a child process several directories below the repo
    // (bin/Release/<tfm>/<job>/bin/Release/<tfm>), so walk up rather than use fixed relative paths.
    private static string FindFont(string name)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tests", "KernSmith.Tests", "Fixtures", name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException($"Test font not found: {name}");
    }
}
