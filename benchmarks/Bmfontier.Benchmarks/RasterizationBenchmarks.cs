using BenchmarkDotNet.Attributes;
using Bmfontier;
using Bmfontier.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Rasterization")]
public class RasterizationBenchmarks
{
    private byte[] _fontData = null!;
    private byte[] _variableFontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
        _variableFontData = File.ReadAllBytes(FindFont("RobotoFlex-Variable.ttf"));
    }

    // Size scaling
    [Benchmark(Description = "16px ASCII")]
    public BmFontResult Size16() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 16,
        Characters = CharacterSet.Ascii
    });

    [Benchmark(Description = "32px ASCII")]
    public BmFontResult Size32() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii
    });

    [Benchmark(Description = "64px ASCII")]
    public BmFontResult Size64() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 64,
        Characters = CharacterSet.Ascii
    });

    [Benchmark(Description = "128px ASCII")]
    public BmFontResult Size128() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 128,
        Characters = CharacterSet.Ascii
    });

    // Glyph count scaling
    [Benchmark(Description = "32px ExtendedASCII (224 chars)")]
    public BmFontResult ExtendedAscii() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.ExtendedAscii
    });

    [Benchmark(Description = "32px Latin (431 chars)")]
    public BmFontResult Latin() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Latin
    });

    // SDF
    [Benchmark(Description = "32px SDF")]
    public BmFontResult Sdf() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Sdf = true
    });

    // AA modes
    [Benchmark(Description = "32px Mono (no AA)")]
    public BmFontResult Mono() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        AntiAlias = AntiAliasMode.None
    });

    [Benchmark(Description = "32px Light AA")]
    public BmFontResult LightAA() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        AntiAlias = AntiAliasMode.Light
    });

    // Bold + Italic
    [Benchmark(Description = "32px Bold")]
    public BmFontResult Bold() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Bold = true
    });

    [Benchmark(Description = "32px Bold+Italic")]
    public BmFontResult BoldItalic() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Bold = true,
        Italic = true
    });

    // Super sampling
    [Benchmark(Description = "32px SuperSample 2x")]
    public BmFontResult SS2() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        SuperSampleLevel = 2
    });

    [Benchmark(Description = "32px SuperSample 4x")]
    public BmFontResult SS4() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        SuperSampleLevel = 4
    });

    // Variable font
    [Benchmark(Description = "32px Variable font (default axes)")]
    public BmFontResult VariableDefault() => BmFont.Generate(_variableFontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii
    });

    [Benchmark(Description = "32px Variable font (custom axes)")]
    public BmFontResult VariableCustom() => BmFont.Generate(_variableFontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        VariationAxes = new Dictionary<string, float> { ["wght"] = 700, ["wdth"] = 125 }
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
