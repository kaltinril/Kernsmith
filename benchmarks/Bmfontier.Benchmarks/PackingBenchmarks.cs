using BenchmarkDotNet.Attributes;
using Bmfontier;
using Bmfontier.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Packing")]
public class PackingBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    // ASCII (95 glyphs)
    [Benchmark(Description = "ASCII MaxRects")]
    public BmFontResult AsciiMaxRects() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = PackingAlgorithm.MaxRects
    });

    [Benchmark(Description = "ASCII Skyline")]
    public BmFontResult AsciiSkyline() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = PackingAlgorithm.Skyline
    });

    // ExtendedAscii (224 glyphs)
    [Benchmark(Description = "ExtendedASCII MaxRects")]
    public BmFontResult ExtAsciiMaxRects() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii,
        PackingAlgorithm = PackingAlgorithm.MaxRects
    });

    [Benchmark(Description = "ExtendedASCII Skyline")]
    public BmFontResult ExtAsciiSkyline() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii,
        PackingAlgorithm = PackingAlgorithm.Skyline
    });

    // Latin (431 glyphs)
    [Benchmark(Description = "Latin MaxRects")]
    public BmFontResult LatinMaxRects() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin,
        PackingAlgorithm = PackingAlgorithm.MaxRects
    });

    [Benchmark(Description = "Latin Skyline")]
    public BmFontResult LatinSkyline() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin,
        PackingAlgorithm = PackingAlgorithm.Skyline
    });

    // Varying max texture size
    [Benchmark(Description = "ASCII MaxRects 256px")]
    public BmFontResult MaxRects256() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = PackingAlgorithm.MaxRects,
        MaxTextureSize = 256
    });

    [Benchmark(Description = "ASCII MaxRects 512px")]
    public BmFontResult MaxRects512() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = PackingAlgorithm.MaxRects,
        MaxTextureSize = 512
    });

    [Benchmark(Description = "ASCII MaxRects 1024px")]
    public BmFontResult MaxRects1024() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = PackingAlgorithm.MaxRects,
        MaxTextureSize = 1024
    });

    private static string FindFont(string name)
    {
        var paths = new[]
        {
            Path.Combine("tests", "Bmfontier.Tests", "Fixtures", name),
            Path.Combine("..", "..", "..", "..", "tests", "Bmfontier.Tests", "Fixtures", name),
        };
        return paths.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Test font not found: {name}");
    }
}
