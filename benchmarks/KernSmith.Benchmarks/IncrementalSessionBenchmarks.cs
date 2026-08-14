using BenchmarkDotNet.Attributes;
using KernSmith;
using KernSmith.Output;
using KernSmith.Output.Model;

/// <summary>
/// Phase 183 gate: a 1-glyph <c>AddGlyphs</c> into a warm ~95-char session must be far cheaper
/// than a full regenerate, with allocations proportional to the glyphs added — not to the font
/// or the atlas.
/// </summary>
/// <remarks>
/// The warm-session benchmark rebuilds the session in <c>[IterationSetup]</c> (unmeasured) and
/// adds exactly one not-yet-present glyph in the body, with <c>InvocationCount = 1</c> so the
/// body runs once per iteration. The alternative — drawing one fresh codepoint per invocation
/// from a pool — was rejected: BenchmarkDotNet runs thousands of invocations per iteration,
/// which exhausts any finite pool, and re-adding a present char is a no-op that would
/// benchmark the wrong thing.
/// </remarks>
[MemoryDiagnoser]
[InvocationCount(1, 1)]
public class IncrementalSessionBenchmarks
{
    // U+0100 (Ā, Latin Extended-A): present in Arial/DejaVu/Roboto, outside ASCII.
    private const string NewGlyph = "\u0100";

    private static readonly string AsciiChars =
        string.Concat(Enumerable.Range(32, 95).Select(cp => (char)cp));

    private byte[] _fontData = null!;
    private BmFontModel _asciiModel = null!;
    private BmFontIncrementalSession? _warmSession;

    private static FontGeneratorOptions Options() => new()
    {
        Size = 32,
        Characters = CharacterSet.Ascii
    };

    [GlobalSetup]
    public void Setup()
    {
        _fontData = File.ReadAllBytes(FindFont());
        _asciiModel = BmFont.Generate(_fontData, Options()).Model;
    }

    [IterationSetup(Target = nameof(AddOneGlyphWarmSession))]
    public void SetupWarmSession()
    {
        _warmSession?.Dispose();
        _warmSession = BmFont.BeginIncremental(_fontData, Options(), AdditionOverflowPolicy.NewPage);
        _warmSession.AddGlyphs(AsciiChars);
    }

    [GlobalCleanup]
    public void Cleanup() => _warmSession?.Dispose();

    [Benchmark(Baseline = true, Description = "Full Generate, printable ASCII")]
    public BmFontResult Generate96Chars()
        => BmFont.Generate(_fontData, Options());

    [Benchmark(Description = "AddGlyphs, 1 glyph, warm session")]
    public GlyphAdditionResult AddOneGlyphWarmSession()
        => _warmSession!.AddGlyphs(NewGlyph);

    [Benchmark(Description = "ResumeIncremental + 1 add")]
    public GlyphAdditionResult ResumeFromModel()
    {
        using var session = BmFont.ResumeIncremental(
            _fontData, Options(), _asciiModel, AdditionOverflowPolicy.NewPage);
        return session.AddGlyphs(NewGlyph);
    }

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
