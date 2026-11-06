using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// <see cref="FaceCache"/>: opt-in reuse of the resolved font, parsed face and loaded rasterizer.
/// The cache is process-wide, so these run serially with the other rasterizer-factory tests.
/// </summary>
[Collection("RasterizerFactory")]
public class FaceCacheTests : IDisposable
{
    private static readonly string FontPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf");

    public FaceCacheTests() => FaceCache.Enabled = false;

    public void Dispose() => FaceCache.Enabled = false;

    private static FontGeneratorOptions[] Builds(RasterizerBackend backend) =>
    [
        new() { Size = 16, Backend = backend },
        new() { Size = 32, Backend = backend, Characters = CharacterSet.FromChars("Hello, World") },
        new() { Size = 24, Backend = backend, Bold = true, Outline = 2 },
        new() { Size = 32, Backend = backend, Italic = true, Characters = CharacterSet.FromChars("Hello, World") },
        new() { Size = 20, Backend = backend, Characters = CharacterSet.FromChars("abc") },
    ];

    private static void ShouldMatch(BmFontResult actual, BmFontResult expected, string label)
    {
        actual.FntText.ShouldBe(expected.FntText, label);
        actual.Pages.Count.ShouldBe(expected.Pages.Count, label);
        for (var i = 0; i < expected.Pages.Count; i++)
        {
            actual.Pages[i].Width.ShouldBe(expected.Pages[i].Width, label);
            actual.Pages[i].Height.ShouldBe(expected.Pages[i].Height, label);
            actual.Pages[i].PixelData.SequenceEqual(expected.Pages[i].PixelData)
                .ShouldBeTrue($"{label}: page {i} pixels differ");
        }
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
    public void Generate_FromPath_CacheOnMatchesCacheOff(RasterizerBackend backend)
    {
        var expected = Builds(backend).Select(o => BmFont.Generate(FontPath, o)).ToList();

        FaceCache.Enabled = true;
        var hitsBefore = FaceCache.Hits;
        // Twice over, so every build after the first pass runs on a reused face.
        for (var pass = 0; pass < 2; pass++)
        {
            var options = Builds(backend);
            for (var i = 0; i < options.Length; i++)
                ShouldMatch(BmFont.Generate(FontPath, options[i]), expected[i], $"{backend} build {i} pass {pass}");
        }

        FaceCache.Hits.ShouldBeGreaterThan(hitsBefore, "the cache was never used");
    }

    [Fact]
    public void Generate_CacheOff_NeverUsesTheCache()
    {
        var hitsBefore = FaceCache.Hits;
        BmFont.Generate(FontPath, new FontGeneratorOptions { Size = 20 });
        BmFont.Generate(FontPath, new FontGeneratorOptions { Size = 20 });
        FaceCache.Hits.ShouldBe(hitsBefore);
    }

    [Fact]
    public void GenerateFromSystem_RegisteredFont_CacheOnMatchesCacheOff()
    {
        const string family = "FaceCacheTestFamily";
        BmFont.RegisterFont(family, File.ReadAllBytes(FontPath));
        try
        {
            var options = () => new FontGeneratorOptions { Size = 24, Characters = CharacterSet.FromChars("Hello") };
            var expected = BmFont.GenerateFromSystem(family, options());

            FaceCache.Enabled = true;
            var hitsBefore = FaceCache.Hits;
            ShouldMatch(BmFont.GenerateFromSystem(family, options()), expected, "first");
            ShouldMatch(BmFont.GenerateFromSystem(family, options()), expected, "second");
            FaceCache.Hits.ShouldBeGreaterThan(hitsBefore);
        }
        finally
        {
            BmFont.UnregisterFont(family);
        }
    }

    [Fact]
    public void RegisterFont_Replacing_DropsTheCachedFace()
    {
        const string family = "FaceCacheSwapFamily";
        var roboto = File.ReadAllBytes(FontPath);
        var noto = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans-Regular-Latin.ttf"));
        FontGeneratorOptions Options() => new() { Size = 24, Characters = CharacterSet.FromChars("Hello") };

        FaceCache.Enabled = true;
        try
        {
            BmFont.RegisterFont(family, roboto);
            var first = BmFont.GenerateFromSystem(family, Options());
            BmFont.GenerateFromSystem(family, Options());

            BmFont.RegisterFont(family, noto);
            var second = BmFont.GenerateFromSystem(family, Options());

            second.FntText.ShouldNotBe(first.FntText, "the replaced font was served from the cache");
        }
        finally
        {
            BmFont.UnregisterFont(family);
        }
    }

    [Fact]
    public void Clear_DropsLoadedFaces()
    {
        FaceCache.Enabled = true;
        BmFont.Generate(FontPath, new FontGeneratorOptions { Size = 20 });
        BmFont.Generate(FontPath, new FontGeneratorOptions { Size = 20 });

        FaceCache.Clear();
        var hitsBefore = FaceCache.Hits;
        BmFont.Generate(FontPath, new FontGeneratorOptions { Size = 20 });
        FaceCache.Hits.ShouldBe(hitsBefore, "a cleared cache served a face");
    }

    [Fact]
    public void Generate_VariationAxes_BypassTheCache()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RobotoFlex-Variable.ttf");
        FontGeneratorOptions Options(float wght) => new()
        {
            Size = 24,
            Characters = CharacterSet.FromChars("Hi"),
            VariationAxes = new Dictionary<string, float> { ["wght"] = wght },
        };

        var light = BmFont.Generate(path, Options(100));
        var heavy = BmFont.Generate(path, Options(900));

        FaceCache.Enabled = true;
        var hitsBefore = FaceCache.Hits;
        ShouldMatch(BmFont.Generate(path, Options(100)), light, "light");
        ShouldMatch(BmFont.Generate(path, Options(900)), heavy, "heavy");
        ShouldMatch(BmFont.Generate(path, Options(100)), light, "light again");
        FaceCache.Hits.ShouldBe(hitsBefore);
    }

    [Fact]
    public void Generate_ConcurrentFromOneCachedFace_ProducesCorrectOutput()
    {
        FontGeneratorOptions Options(int size) => new() { Size = size, Characters = CharacterSet.FromChars("Hello, World") };
        var sizes = new[] { 16, 20, 24, 28, 32, 36 };
        var expected = sizes.ToDictionary(s => s, s => BmFont.Generate(FontPath, Options(s)));

        FaceCache.Enabled = true;
        BmFont.Generate(FontPath, Options(16)); // warm: one face in the cache

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 48, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var size = sizes[i % sizes.Length];
            try
            {
                ShouldMatch(BmFont.Generate(FontPath, Options(size)), expected[size], $"size {size} iteration {i}");
            }
            catch (Exception ex)
            {
                failures.Add(ex.Message);
            }
        });

        failures.ShouldBeEmpty();
    }
}
