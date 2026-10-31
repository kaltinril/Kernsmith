using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;

/// <summary>
/// The output stage a caller pays after <c>BmFont.Generate</c>: encoding each atlas page
/// (PNG, TGA, DDS) and formatting the <c>.fnt</c> descriptor (text, XML, binary). Generation
/// runs once in setup, so each row is the output stage alone.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Output")]
public class OutputBenchmarks
{
    private byte[] _fontData = null!;
    private BmFontResult _result = null!;

    /// <summary>ASCII (95 glyphs, small page) or ASCII + Latin-1 (224 glyphs, a larger page).</summary>
    [Params("ascii", "latin1")]
    public string Charset { get; set; } = "ascii";

    [GlobalSetup]
    public void Setup()
    {
        _fontData = BenchmarkFixtures.ReadFont("Roboto-Regular.ttf");
        _result = BmFont.Generate(_fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = Charset == "ascii" ? CharacterSet.Ascii : CharacterSet.ExtendedAscii,
        });
    }

    [Benchmark(Description = "PNG encode")]
    public byte[][] Png() => _result.GetPngData();

    [Benchmark(Description = "TGA encode")]
    public byte[][] Tga() => _result.GetTgaData();

    [Benchmark(Description = "DDS encode")]
    public byte[][] Dds() => _result.GetDdsData();

    [Benchmark(Description = ".fnt text")]
    public string FntText() => new TextFormatter().FormatText(_result.Model);

    [Benchmark(Description = ".fnt XML")]
    public string FntXml() => new XmlFormatter().FormatText(_result.Model);

    [Benchmark(Description = ".fnt binary")]
    public byte[] FntBinary() => new BmFontBinaryFormatter().FormatBinary(_result.Model);
}
