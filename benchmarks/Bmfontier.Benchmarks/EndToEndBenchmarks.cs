using BenchmarkDotNet.Attributes;
using Bmfontier;
using Bmfontier.Output;

[MemoryDiagnoser]
[BenchmarkCategory("EndToEnd")]
public class EndToEndBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    [Benchmark(Description = "Game UI: ASCII 32px, outline 2px, gradient")]
    public BmFontResult GameUi() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Outline = 2,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0,
        GradientStartR = 255,
        GradientStartG = 255,
        GradientStartB = 255,
        GradientEndR = 180,
        GradientEndG = 180,
        GradientEndB = 180,
        GradientAngle = 90f
    });

    [Benchmark(Description = "Dialogue: Latin 24px, no effects")]
    public BmFontResult Dialogue() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 24,
        Characters = CharacterSet.Latin
    });

    [Benchmark(Description = "Title: ASCII 96px, outline+shadow+gradient, SS 2x")]
    public BmFontResult Title() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 96,
        Characters = CharacterSet.Ascii,
        SuperSampleLevel = 2,
        Outline = 3,
        OutlineR = 0,
        OutlineG = 0,
        OutlineB = 0,
        GradientStartR = 255,
        GradientStartG = 215,
        GradientStartB = 0,
        GradientEndR = 255,
        GradientEndG = 140,
        GradientEndB = 0,
        GradientAngle = 90f,
        ShadowOffsetX = 3,
        ShadowOffsetY = 3,
        ShadowR = 0,
        ShadowG = 0,
        ShadowB = 0,
        ShadowOpacity = 0.6f,
        ShadowBlur = 4
    });

    [Benchmark(Description = "SDF Atlas: ASCII 48px, SDF")]
    public BmFontResult SdfAtlas() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 48,
        Characters = CharacterSet.Ascii,
        Sdf = true
    });

    [Benchmark(Description = "Channel Packed: ASCII 32px")]
    public BmFontResult ChannelPacked() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        ChannelPacking = true
    });

    [Benchmark(Description = "Stress Test: Latin 128px, SS 4x")]
    public BmFontResult StressTest() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 128,
        Characters = CharacterSet.Latin,
        SuperSampleLevel = 4
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
