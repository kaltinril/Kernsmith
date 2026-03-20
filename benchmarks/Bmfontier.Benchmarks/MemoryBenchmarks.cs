using BenchmarkDotNet.Attributes;
using Bmfontier;
using Bmfontier.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Memory")]
public class MemoryBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    // Scaling glyph count to see per-glyph allocation cost
    [Benchmark(Description = "1 glyph (allocation baseline)")]
    public BmFontResult Glyph1() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.FromChars("A")
    });

    [Benchmark(Description = "10 glyphs")]
    public BmFontResult Glyph10() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABCDEFGHIJ")
    });

    [Benchmark(Description = "95 glyphs (ASCII)")]
    public BmFontResult Glyph95() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii
    });

    [Benchmark(Description = "224 glyphs (Extended)")]
    public BmFontResult Glyph224() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii
    });

    [Benchmark(Description = "431 glyphs (Latin)")]
    public BmFontResult Glyph431() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin
    });

    // Atlas size impact on allocation
    [Benchmark(Description = "256x256 max texture")]
    public BmFontResult Texture256() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        MaxTextureSize = 256
    });

    [Benchmark(Description = "1024x1024 max texture")]
    public BmFontResult Texture1024() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        MaxTextureSize = 1024
    });

    [Benchmark(Description = "2048x2048 max texture")]
    public BmFontResult Texture2048() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 64,
        Characters = CharacterSet.Ascii,
        MaxTextureSize = 2048
    });

    // Effects add RGBA allocations
    [Benchmark(Description = "With effects (outline+gradient)")]
    public BmFontResult WithEffects() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABCDEFGHIJ"),
        Outline = 3,
        GradientStartR = 255,
        GradientStartG = 0,
        GradientStartB = 0,
        GradientEndR = 0,
        GradientEndG = 0,
        GradientEndB = 255
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
