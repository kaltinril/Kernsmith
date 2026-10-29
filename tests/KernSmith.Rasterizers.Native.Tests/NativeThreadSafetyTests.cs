using System.Collections.Concurrent;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 — parallel generation. A <see cref="NativeRasterizer"/> instance is not thread-safe,
/// and core never shares one: every <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/>
/// (and every <see cref="BmFont.GenerateBatch"/> job) creates its own through
/// <c>RasterizerFactory</c> and rasterizes on one thread; only the post-rasterization steps
/// (effects, super-sample downscale, page encoding) run <c>Parallel.For</c>, over bitmaps.
/// What must hold, then, is that nothing Native or core keeps in static state — the lazily
/// loaded Brotli dictionary, renderer singletons, lookup tables — lets concurrent generations
/// disturb each other. These tests generate the same fonts serially and concurrently and require
/// byte-identical output.
/// </summary>
public class NativeThreadSafetyTests
{
    private const string Text = "AgOé&Å1 .Wy";

    /// <summary>Fonts and options covering each decoder and renderer: glyf, CFF, CFF2 and WOFF2 sources; coverage, mono, LCD, hinting, SDF, MSDF, styles, variations and both COLR versions.</summary>
    private static readonly (string Name, Func<byte[]> Font, Func<FontGeneratorOptions> Options)[] Jobs =
    [
        ("Roboto 32", TestFonts.RobotoRegularBytes, () => Options(32)),
        ("Roboto 15 mono", TestFonts.RobotoRegularBytes, () => Options(15, o => o.AntiAlias = AntiAliasMode.None)),
        ("Roboto 20 LCD", TestFonts.RobotoRegularBytes, () => Options(20, o => o.AntiAlias = AntiAliasMode.Lcd)),
        ("Roboto WOFF2 MSDF", () => Fixture("Roboto-Regular.woff2"), () => Options(28, o => o.SdfMode = SdfMode.Mtsdf)),
        ("SourceSerif bold italic", TestFonts.SourceSerifOtfBytes, () => Options(24, o => { o.Bold = true; o.Italic = true; })),
        ("SourceSerif light hinting", TestFonts.SourceSerifOtfBytes, () => Options(12, o => { o.AntiAlias = AntiAliasMode.Light; o.EnableHinting = true; })),
        ("SourceCodeVF wght 900", TestFonts.SourceCodeVariableOtfBytes, () => Options(22, o => o.VariationAxes = new() { ["wght"] = 900 })),
        ("RobotoFlex SDF", TestFonts.RobotoFlexBytes, () => Options(30, o => { o.Sdf = true; o.VariationAxes = new() { ["wght"] = 1000, ["wdth"] = 25 }; })),
        ("NotoSans CFF WOFF2", () => Fixture("NotoSans-Regular-Latin-CFF.woff2"), () => Options(26)),
        ("BungeeColor palette 2", TestFonts.BungeeColorBytes, () => Options(36, o => { o.ColorFont = true; o.ColorPaletteIndex = 2; })),
        ("ReemKufiInk COLRv1", TestFonts.ReemKufiInkBytes, () => Options(40, o => { o.ColorFont = true; o.Characters = CharacterSet.FromChars("AO\u0628\u0639\u0661"); })),
    ];

    [Fact]
    public void ConcurrentGenerate_MatchesSerialOutputByteForByte()
    {
        var serial = Jobs.Select(job => Snapshot(BmFont.Generate(job.Font(), job.Options()))).ToArray();
        foreach (var (snapshot, i) in serial.Select((s, i) => (s, i)))
            snapshot.Pixels.ShouldContain(page => page.Any(b => b != 0), $"{Jobs[i].Name} should draw something");

        // Every job four times over, interleaved, on as many threads as the machine offers.
        const int Repeats = 4;
        var concurrent = new ConcurrentBag<(int Job, Output Snapshot)>();
        Parallel.For(0, Jobs.Length * Repeats, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(4, Environment.ProcessorCount) }, n =>
        {
            int job = n % Jobs.Length;
            concurrent.Add((job, Snapshot(BmFont.Generate(Jobs[job].Font(), Jobs[job].Options()))));
        });

        concurrent.Count.ShouldBe(Jobs.Length * Repeats);
        foreach (var (job, snapshot) in concurrent)
            snapshot.ShouldBeSameAs(serial[job], Jobs[job].Name);
    }

    [Fact]
    public void ParallelBatch_MatchesSerialOutputByteForByte()
    {
        var jobs = Jobs.Select(job => new BatchJob { FontData = job.Font(), Options = job.Options() }).ToList();

        var serial = BmFont.GenerateBatch(jobs, new BatchOptions { MaxParallelism = 1 });
        var parallel = BmFont.GenerateBatch(jobs, new BatchOptions { MaxParallelism = Math.Max(4, Environment.ProcessorCount) });

        for (int i = 0; i < jobs.Count; i++)
        {
            serial.Results[i].Success.ShouldBeTrue($"{Jobs[i].Name}: {serial.Results[i].Error}");
            parallel.Results[i].Success.ShouldBeTrue($"{Jobs[i].Name}: {parallel.Results[i].Error}");
            Snapshot(parallel.Results[i].Result!).ShouldBeSameAs(Snapshot(serial.Results[i].Result!), Jobs[i].Name);
        }
    }

    private static FontGeneratorOptions Options(int size, Action<FontGeneratorOptions>? configure = null)
    {
        var options = new FontGeneratorOptions
        {
            Size = size,
            Characters = CharacterSet.FromChars(Text),
            Backend = RasterizerBackend.Native,
        };
        configure?.Invoke(options);
        return options;
    }

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static Output Snapshot(BmFontResult result) =>
        new(result.FntText, [.. result.Pages.Select(p => p.PixelData)]);

    private sealed record Output(string Fnt, byte[][] Pixels)
    {
        public void ShouldBeSameAs(Output expected, string job)
        {
            Fnt.ShouldBe(expected.Fnt, $"{job}: .fnt");
            Pixels.Length.ShouldBe(expected.Pixels.Length, $"{job}: page count");
            for (int i = 0; i < Pixels.Length; i++)
                Pixels[i].SequenceEqual(expected.Pixels[i]).ShouldBeTrue($"{job}: page {i} pixels differ");
        }
    }
}
