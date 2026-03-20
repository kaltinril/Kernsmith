using BenchmarkDotNet.Attributes;
using Bmfontier;
using Bmfontier.Output;

[MemoryDiagnoser]
[BenchmarkCategory("Encoding")]
public class EncodingBenchmarks
{
    private byte[] _fontData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont("Roboto-Regular.ttf"));
    }

    // PNG
    [Benchmark(Description = "PNG 256px")]
    public BmFontResult Png256() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Png,
        MaxTextureSize = 256
    });

    [Benchmark(Description = "PNG 512px")]
    public BmFontResult Png512() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Png,
        MaxTextureSize = 512
    });

    [Benchmark(Description = "PNG 1024px")]
    public BmFontResult Png1024() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Png,
        MaxTextureSize = 1024
    });

    // TGA
    [Benchmark(Description = "TGA 256px")]
    public BmFontResult Tga256() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Tga,
        MaxTextureSize = 256
    });

    [Benchmark(Description = "TGA 512px")]
    public BmFontResult Tga512() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Tga,
        MaxTextureSize = 512
    });

    [Benchmark(Description = "TGA 1024px")]
    public BmFontResult Tga1024() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Tga,
        MaxTextureSize = 1024
    });

    // DDS
    [Benchmark(Description = "DDS 256px")]
    public BmFontResult Dds256() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Dds,
        MaxTextureSize = 256
    });

    [Benchmark(Description = "DDS 512px")]
    public BmFontResult Dds512() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Dds,
        MaxTextureSize = 512
    });

    [Benchmark(Description = "DDS 1024px")]
    public BmFontResult Dds1024() => BmFont.Generate(_fontData, new FontGeneratorOptions
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        TextureFormat = TextureFormat.Dds,
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
