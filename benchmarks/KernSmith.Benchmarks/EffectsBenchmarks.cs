using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Effects")]
public class EffectsBenchmarks
{
    private byte[] _fontData = null!;
    private CharacterSet _chars = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
        _chars = CharacterSet.FromChars("ABCDEFGHIJ");
    }

    [Benchmark(Description = "Baseline (no effects)")]
    public BmFontResult Baseline() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars
    });

    [Benchmark(Description = "Outline width 1")]
    public BmFontResult Outline1() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        Outline = 1,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0
    });

    [Benchmark(Description = "Outline width 3")]
    public BmFontResult Outline3() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        Outline = 3,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0
    });

    [Benchmark(Description = "Outline width 6")]
    public BmFontResult Outline6() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        Outline = 6,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0
    });

    [Benchmark(Description = "Gradient only (90 deg)")]
    public BmFontResult Gradient() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        GradientStartR = 255,
        GradientStartG = 0,
        GradientStartB = 0,
        GradientEndR = 0,
        GradientEndG = 0,
        GradientEndB = 255,
        GradientAngle = 90f,
        GradientMidpoint = 0.5f
    });

    [Benchmark(Description = "Shadow (no blur)")]
    public BmFontResult ShadowNoBlur() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        ShadowOffsetX = 2,
        ShadowOffsetY = 2,
        ShadowR = 0,
        ShadowG = 0,
        ShadowB = 0,
        ShadowOpacity = 0.8f,
        ShadowBlur = 0
    });

    [Benchmark(Description = "Shadow (blur=4)")]
    public BmFontResult ShadowBlur4() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        ShadowOffsetX = 2,
        ShadowOffsetY = 2,
        ShadowR = 0,
        ShadowG = 0,
        ShadowB = 0,
        ShadowOpacity = 0.8f,
        ShadowBlur = 4
    });

    [Benchmark(Description = "Full stack (outline+gradient+shadow)")]
    public BmFontResult FullStack() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = _chars,
        Outline = 3,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0,
        GradientStartR = 255,
        GradientStartG = 200,
        GradientStartB = 0,
        GradientEndR = 255,
        GradientEndG = 0,
        GradientEndB = 100,
        GradientAngle = 90f,
        GradientMidpoint = 0.5f,
        ShadowOffsetX = 2,
        ShadowOffsetY = 2,
        ShadowR = 0,
        ShadowG = 0,
        ShadowB = 0,
        ShadowOpacity = 0.7f,
        ShadowBlur = 4
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
