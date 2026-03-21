using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Comparative")]
public class ComparativeBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    [Params(PackingAlgorithm.MaxRects, PackingAlgorithm.Skyline)]
    public PackingAlgorithm Packer { get; set; }

    [Benchmark(Description = "95 glyphs")]
    public BmFontResult Pack95() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        PackingAlgorithm = Packer
    });

    [Benchmark(Description = "224 glyphs")]
    public BmFontResult Pack224() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii,
        PackingAlgorithm = Packer
    });

    [Benchmark(Description = "431 glyphs")]
    public BmFontResult Pack431() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin,
        PackingAlgorithm = Packer
    });

    private static string FindFont(string name)
    {
        var paths = new[]
        {
            Path.Combine("tests", "KernSmith.Tests", "Fixtures", name),
            Path.Combine("..", "..", "..", "..", "tests", "KernSmith.Tests", "Fixtures", name),
        };
        return paths.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Test font not found: {name}");
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("Comparative")]
public class EncoderComparativeBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    [Params(TextureFormat.Png, TextureFormat.Tga, TextureFormat.Dds)]
    public TextureFormat Format { get; set; }

    [Benchmark(Description = "ASCII 32px")]
    public BmFontResult Ascii32() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = Format
    });

    [Benchmark(Description = "Latin 32px")]
    public BmFontResult Latin32() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin,
        TextureFormat = Format
    });

    private static string FindFont(string name)
    {
        var paths = new[]
        {
            Path.Combine("tests", "KernSmith.Tests", "Fixtures", name),
            Path.Combine("..", "..", "..", "..", "tests", "KernSmith.Tests", "Fixtures", name),
        };
        return paths.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Test font not found: {name}");
    }
}

[MemoryDiagnoser]
[BenchmarkCategory("Comparative")]
public class SuperSampleComparativeBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    [Params(1, 2, 3, 4)]
    public int SuperSample { get; set; }

    [Benchmark(Description = "ASCII 32px")]
    public BmFontResult Ascii32() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        SuperSampleLevel = SuperSample
    });

    private static string FindFont(string name)
    {
        var paths = new[]
        {
            Path.Combine("tests", "KernSmith.Tests", "Fixtures", name),
            Path.Combine("..", "..", "..", "..", "tests", "KernSmith.Tests", "Fixtures", name),
        };
        return paths.Select(Path.GetFullPath).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Test font not found: {name}");
    }
}
