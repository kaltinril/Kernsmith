using System.Globalization;
using KernSmith.Rasterizers.Native.Tests.Fuzzing;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 — hostile input. Each fixture font is damaged table by table (truncation, bit
/// flips, offset corruption, count inflation, random byte runs) and run through LoadFont, font
/// and glyph metrics and RasterizeGlyph under rotating option combinations (mono, LCD, light
/// hinting, SDF/MSDF/MTSDF, synthetic bold+italic, color, a variation instance). The only
/// acceptable outcomes are success or the library's documented exceptions; see
/// <see cref="FuzzRunner"/> for the exact contract.
/// </summary>
/// <remarks>
/// Deterministic: case <c>n</c> of a fixture always applies the same mutation, so a failure
/// message reproduces exactly. The committed iteration count visits every (table, mutation)
/// pair at least once and keeps the class to roughly ten seconds; set <c>KERNSMITH_FUZZ_ITERATIONS</c> for a longer manual run and
/// <c>KERNSMITH_FUZZ_SEED</c> to explore a different deterministic sequence.
/// </remarks>
public class NativeFuzzTests
{
    private const int DefaultIterations = 120;

    /// <summary>Every table Native reads, plus the directory that locates them.</summary>
    private static readonly string[] NativeTables =
    [
        FontMutator.Directory, "head", "hhea", "maxp", "hmtx", "OS/2", "cmap", "loca", "glyf",
        "CFF ", "CFF2", "fvar", "avar", "gvar", "HVAR", "MVAR", "COLR", "CPAL", "CBLC", "CBDT", "sbix",
    ];

    private static readonly int[] LatinSample =
        [.. "AgOé&Å1 .".Select(c => (int)c), 0x0301, 0xE000, 0x10FFFF];

    [Theory]
    [InlineData("Roboto")]
    [InlineData("SourceSerif4")]
    [InlineData("SourceCodeVF")]
    [InlineData("RobotoFlex")]
    [InlineData("BungeeColor")]
    [InlineData("ReemKufiInk")]
    [InlineData("NotoColorEmoji")]
    [InlineData("SyntheticCbdt")]
    [InlineData("SyntheticSbix")]
    public void DamagedFont_FailsOnlyWithDocumentedExceptions(string fixture)
    {
        var profile = Profile(fixture);
        var mutator = new FontMutator(profile.Font, profile.Sample, profile.Tables);

        Sweep(fixture, mutator.Tables, (table, kind, rng) => (mutator.Apply(table, kind, rng, out string d), d),
            (caseName, damaged, i) => FuzzRunner.Run(caseName, damaged, profile, i),
            iterationScale: 1.0);
    }

    [Theory]
    [InlineData("Roboto-Regular.woff2")]
    [InlineData("NotoSans-Regular-Latin.woff2")]
    [InlineData("RobotoFlex-Latin.woff2")]
    [InlineData("NotoSans-Regular-Latin-CFF.woff2")]
    public void DamagedWoff2_ThroughCore_FailsOnlyWithDocumentedExceptions(string fixture)
    {
        var mutator = new Woff2Mutator(Fixture(fixture));
        var instance = fixture.StartsWith("RobotoFlex", StringComparison.Ordinal)
            ? NativeVariableFontTests.Axes("wght=1000,wdth=25")
            : null;

        Sweep(fixture, mutator.Regions, (region, kind, rng) => (mutator.Apply(region, kind, rng, out string d), d),
            (caseName, damaged, i) => FuzzRunner.RunGenerate(caseName, damaged, CoreText, instance, i),
            iterationScale: 0.25);
    }

    [Theory]
    [InlineData("Roboto-Regular.ttf")]
    [InlineData("RobotoFlex-Variable.ttf")]
    [InlineData("SourceSerif4-Regular.otf")]
    public void DamagedFont_ThroughCore_FailsOnlyWithDocumentedExceptions(string fixture)
    {
        var font = Fixture(fixture);
        var mutator = new FontMutator(font, CoreText.Select(c => (int)c), CoreTables);
        var instance = fixture.StartsWith("RobotoFlex", StringComparison.Ordinal)
            ? NativeVariableFontTests.Axes("wght=1000,wdth=25")
            : null;

        Sweep(fixture, mutator.Tables, (table, kind, rng) => (mutator.Apply(table, kind, rng, out string d), d),
            (caseName, damaged, i) => FuzzRunner.RunGenerate(caseName, damaged, CoreText, instance, i),
            iterationScale: 0.25);
    }

    /// <summary>The text core-path cases generate: a composite, a descender, a blank, punctuation.</summary>
    private const string CoreText = "AgÅ .";

    /// <summary>The tables core's own parser reads (metrics, names, cmap, kerning, axes) plus the outline tables.</summary>
    private static readonly string[] CoreTables =
    [
        FontMutator.Directory, "head", "hhea", "hmtx", "maxp", "OS/2", "name", "cmap", "kern", "GPOS", "fvar",
        "glyf", "loca", "CFF ",
    ];

    /// <summary>
    /// Visits every (region, kind) pair before repeating, damaging and running one case per
    /// iteration, and fails with every finding grouped by cause.
    /// </summary>
    private static void Sweep(
        string fixture,
        IReadOnlyList<string> regions,
        Func<string, MutationKind, Random, (byte[] Damaged, string Description)> damage,
        Func<string, byte[], int, FuzzFailure?> run,
        double iterationScale)
    {
        int iterations = Math.Max(regions.Count * FontMutator.Kinds.Count, (int)(Iterations() * iterationScale));
        int seedBase = SeedBase();
        var failures = new List<FuzzFailure>();

        for (int i = 0; i < iterations; i++)
        {
            string region = regions[i % regions.Count];
            var kind = FontMutator.Kinds[(i / regions.Count) % FontMutator.Kinds.Count];
            var rng = new Random(seedBase + (i * 7919) + StableHash(fixture));

            var (damaged, description) = damage(region, kind, rng);
            string caseName = $"{fixture} #{i} (seed {seedBase}): {region} {kind} {description}";

            if (run(caseName, damaged, i) is { } failure)
                failures.Add(failure);
        }

        failures.ShouldBeEmpty(Report(failures));
    }

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    internal static string Report(List<FuzzFailure> failures)
    {
        var groups = failures.GroupBy(f => f.Signature).ToList();
        return $"{failures.Count} failing case(s), {groups.Count} distinct cause(s):\n"
            + string.Join("\n\n", groups.Select(g => $"[{g.Count()}x] {g.First()}"));
    }

    internal static int Iterations() =>
        int.TryParse(Environment.GetEnvironmentVariable("KERNSMITH_FUZZ_ITERATIONS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : DefaultIterations;

    internal static int SeedBase() =>
        int.TryParse(Environment.GetEnvironmentVariable("KERNSMITH_FUZZ_SEED"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s)
            ? s
            : 0;

    /// <summary>A string hash that is the same in every process (string.GetHashCode is randomized).</summary>
    internal static int StableHash(string text)
    {
        int hash = 17;
        foreach (char c in text)
            hash = unchecked((hash * 31) + c);
        return hash & 0x7FFFFFFF;
    }

    private static FuzzProfile Profile(string fixture) => fixture switch
    {
        "Roboto" => new(fixture, TestFonts.RobotoRegularBytes(), LatinSample, null, null, NativeTables),
        "SourceSerif4" => new(fixture, TestFonts.SourceSerifOtfBytes(), LatinSample, null, null, NativeTables),
        "SourceCodeVF" => Variable(fixture, TestFonts.SourceCodeVariableOtfBytes(), "wght=900"),
        "RobotoFlex" => Variable(fixture, TestFonts.RobotoFlexBytes(), "wght=1000,wdth=25,opsz=144"),
        "BungeeColor" => new(fixture, TestFonts.BungeeColorBytes(), [.. "ABO&1 ".Select(c => (int)c), 0xE000], null, null, NativeTables),
        "ReemKufiInk" => new(fixture, TestFonts.ReemKufiInkBytes(),
            [.. "AO1 ".Select(c => (int)c), 0x0628, 0x0639, 0x0661, 0x06F4, 0xE000], null, null, NativeTables),
        "NotoColorEmoji" => new(fixture, TestFonts.NotoColorEmojiBytes(), [0x1F600, 0x2764, 0x2B50, 0x1F680, 0x23, 0x20, 0xE000], null, null, NativeTables, BitmapOnly: true),
        "SyntheticCbdt" => new(fixture, SyntheticBitmapFonts.Cbdt(), LatinSample, null, null, NativeTables, BitmapOnly: true),
        "SyntheticSbix" => new(fixture, SyntheticBitmapFonts.Sbix(), LatinSample, null, null, NativeTables),
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, null),
    };

    private static FuzzProfile Variable(string name, byte[] font, string instance) =>
        new(name, font, LatinSample, NativeVariableFontTests.FvarAxes(font), NativeVariableFontTests.Axes(instance), NativeTables);
}
