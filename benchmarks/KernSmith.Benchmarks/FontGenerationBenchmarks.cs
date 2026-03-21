using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;

[MemoryDiagnoser]
public class FontGenerationBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont());
    }

    [Benchmark(Description = "ASCII, 32px, MaxRects")]
    public BmFontResult AsciiMaxRects()
        => BmFont.Generate(_fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            PackingAlgorithm = PackingAlgorithm.MaxRects
        });

    [Benchmark(Description = "ASCII, 32px, Skyline")]
    public BmFontResult AsciiSkyline()
        => BmFont.Generate(_fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            PackingAlgorithm = PackingAlgorithm.Skyline
        });

    [Benchmark(Description = "ExtendedASCII, 32px")]
    public BmFontResult ExtendedAscii()
        => BmFont.Generate(_fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.ExtendedAscii
        });

    [Benchmark(Description = "ASCII, 64px")]
    public BmFontResult Ascii64()
        => BmFont.Generate(_fontData, new FontGeneratorOptions
        {
            Size = 64,
            Characters = CharacterSet.Ascii
        });

    private static string FindFont()
    {
        var paths = new[]
        {
            @"C:\Windows\Fonts\arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf"
        };
        return paths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("No test font found");
    }
}
