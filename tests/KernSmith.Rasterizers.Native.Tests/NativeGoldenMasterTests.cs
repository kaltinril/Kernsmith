using System.Globalization;
using System.Text;
using KernSmith.Rasterizers.Native.Tests.Goldens;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 golden masters: Native's exact output for every committed fixture font across
/// sizes, anti-alias modes, distance fields, synthetic styles, variation instances and color,
/// plus full <see cref="BmFont.Generate(byte[], FontGeneratorOptions)"/> output for a few
/// configurations. The matrix is <see cref="GoldenCatalog"/>; the expected values are the
/// committed files in <c>Goldens/</c> (see <see cref="GoldenStore"/>).
/// </summary>
/// <remarks>
/// A mismatch fails with the changed glyphs, and writes the actual contact sheet (and, for a
/// case with a committed reference PNG, the expected sheet, a magenta diff and the largest level
/// difference) under <c>GoldenFailures/</c> in the test output folder. A deliberate output change
/// regenerates with <c>KERNSMITH_UPDATE_GOLDENS=1</c> (never in CI) and is reviewed in the diff.
/// The raster cases run from <c>NativeGoldenModeTests</c>, <c>NativeGoldenDistanceFieldTests</c>
/// and <c>NativeGoldenFeatureTests</c>; this class holds the pipeline cases and the guards.
/// Output must be identical on every runtime and architecture: Native uses no SIMD-width-dependent
/// arithmetic, and its few libm calls (Pow/Sin/Cos/Tan/Atan2, all double) round to float or bytes.
/// </remarks>
public class NativeGoldenMasterTests
{
    public static TheoryData<string> GenerateCaseIds() => [.. GoldenCatalog.Generate.Keys];

    /// <summary>The raster cases of one group (each group is its own test class, so they run in parallel).</summary>
    internal static TheoryData<string> RasterCaseIds(GoldenGroup group) =>
        [.. GoldenCatalog.Raster.Values.Where(c => c.Group == group).Select(c => c.Id)];

    /// <summary>Renders raster case <paramref name="id"/> and checks it against its golden
    /// (or, in update mode, records it).</summary>
    internal static void AssertRasterGolden(string id)
    {
        var goldenCase = GoldenCatalog.Raster[id];
        var result = GoldenRenderer.Render(goldenCase);
        result.Glyphs.ShouldContain(g => g.Glyph != null && g.Glyph.Width > 0, $"{id}: rendered no ink at all");

        if (GoldenStore.UpdateMode)
        {
            GoldenStore.Set(id, result.Entry);
            if (goldenCase.Reference)
                GoldenStore.WritePngIfChanged(GoldenStore.PngPath(id), result.Sheet);
            return;
        }

        var expected = GoldenStore.Get(id);
        var reference = goldenCase.Reference ? GoldenStore.ReadPng(GoldenStore.PngPath(id)) : null;
        if (goldenCase.Reference && reference is null)
            Assert.Fail($"{id}: reference PNG {GoldenStore.PngPath(id)} is missing. {GoldenStore.RegenerateHint}");

        if (expected is null)
            Assert.Fail($"{id}: no golden in the manifest. {GoldenStore.RegenerateHint}");

        bool hashMatches = expected.Sha256 == result.Entry.Sha256;
        bool imageMatches = reference is null || SamePixels(reference, result.Sheet);
        if (hashMatches && imageMatches)
            return;

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"Native output for golden '{id}' changed.\n");
        AppendChangedParts(report, expected.Parts, result.Entry.Parts, result.Glyphs);

        string dir = GoldenStore.FailureDirectory(id);
        File.WriteAllBytes(Path.Combine(dir, "actual.png"), GoldenPng.Encode(result.Sheet));
        if (reference is not null)
            AppendImageDiff(report, dir, "", reference, result.Sheet);
        else
            report.Append("No reference PNG is committed for this case, so there is no expected image; the per-glyph list above names what changed.\n");

        report.Append(CultureInfo.InvariantCulture, $"Artifacts: {dir}\n{GoldenStore.RegenerateHint}");
        Assert.Fail(report.ToString());
    }

    [Theory]
    [MemberData(nameof(GenerateCaseIds))]
    public void Generate_MatchesGolden(string id)
    {
        var goldenCase = GoldenCatalog.Generate[id];
        var result = GoldenRenderer.Generate(goldenCase, goldenCase.Font.Bytes());
        result.Pages.ShouldNotBeEmpty($"{id}: no atlas pages");
        result.Pages[0].Pixels.ShouldContain(b => b != 0, $"{id}: the atlas is blank");

        if (GoldenStore.UpdateMode)
        {
            GoldenStore.Set(id, result.Entry);
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenStore.FntPath(id))!);
            File.WriteAllText(GoldenStore.FntPath(id), result.Fnt, new UTF8Encoding(false));
            if (goldenCase.Reference)
            {
                for (int p = 0; p < result.Pages.Count; p++)
                    GoldenStore.WritePngIfChanged(PagePngPath(id, p), result.Pages[p]);
            }

            return;
        }

        AssertGenerateMatches(id, goldenCase, result, input: goldenCase.Font.FileName);
    }

    [Theory]
    [InlineData("Roboto-Regular.woff2", "generate/roboto/grayscale-28")]
    [InlineData("NotoSans-Regular-Latin.woff2", "generate/notosans/grayscale-28")]
    [InlineData("NotoSans-Regular-Latin-CFF.woff2", "generate/notosans-cff/grayscale-28")]
    [InlineData("RobotoFlex-Latin.woff2", "generate/robotoflex-latin/grayscale-28")]
    public void Generate_FromWoff2_MatchesTheTtfGolden(string woff2, string id)
    {
        // Core decodes WOFF2 before Native sees the bytes, so the WOFF2 must reproduce the
        // golden of the font it was made from, not a golden of its own.
        if (GoldenStore.UpdateMode)
            return;

        var goldenCase = GoldenCatalog.Generate[id];
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", woff2));
        AssertGenerateMatches(id, goldenCase, GoldenRenderer.Generate(goldenCase, bytes), input: woff2);
    }

    [Fact]
    public void Manifest_CoversExactlyTheMatrix()
    {
        // No case may pass for want of a golden, and no golden may outlive its case. Files are
        // checked in the source tree: the output folder keeps stale copies of deleted files.
        var live = GoldenCatalog.Raster.Keys.Concat(GoldenCatalog.Generate.Keys).ToHashSet(StringComparer.Ordinal);
        var expectedFiles = GoldenCatalog.Raster.Values.Where(c => c.Reference).Select(c => "png/" + GoldenCatalog.FileName(c.Id) + ".png")
            .Concat(GoldenCatalog.Generate.Values.Where(c => c.Reference).Select(c => "png/" + GoldenCatalog.FileName(c.Id) + "-page0.png"))
            .Concat(GoldenCatalog.Generate.Keys.Select(id => "fnt/" + GoldenCatalog.FileName(id) + ".fnt"))
            .ToHashSet(StringComparer.Ordinal);
        string source = GoldenStore.SourceDirectory();
        var files = GoldenFiles(source).ToHashSet(StringComparer.Ordinal);

        if (GoldenStore.UpdateMode)
        {
            GoldenStore.Prune(live);
            foreach (var orphan in files.Except(expectedFiles))
                File.Delete(Path.Combine(source, orphan));
            return;
        }

        var ids = GoldenStore.Ids.ToHashSet(StringComparer.Ordinal);
        live.Except(ids).ShouldBeEmpty($"cases without a golden. {GoldenStore.RegenerateHint}");
        ids.Except(live).ShouldBeEmpty($"goldens without a case. {GoldenStore.RegenerateHint}");
        files.Except(expectedFiles).ShouldBeEmpty($"orphaned golden files in {source}");
        expectedFiles.Except(files).ShouldBeEmpty($"missing golden files in {source}");
    }

    [Fact]
    public void Matrix_CoversEveryFixtureFontSizeAndMode()
    {
        // Guards the catalog itself against silently shrinking.
        foreach (var font in GoldenCatalog.Fonts)
        {
            foreach (float size in GoldenCatalog.Sizes)
            {
                foreach (var mode in GoldenCatalog.Modes)
                {
                    string id = string.Create(CultureInfo.InvariantCulture, $"{font.Id}/{mode.ToString().ToLowerInvariant()}/{size}px");
                    GoldenCatalog.Raster.Keys.ShouldContain(id);
                }
            }

            foreach (string field in new[] { "sdf", "msdf", "mtsdf" })
                GoldenCatalog.Raster.Keys.Count(k => k.StartsWith($"{font.Id}/{field}/", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2, $"{font.Id} {field}");

            foreach (string style in new[] { "bold", "italic", "bold-italic" })
                GoldenCatalog.Raster.Keys.Count(k => k.StartsWith($"{font.Id}/", StringComparison.Ordinal) && k.EndsWith("/" + style, StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2, $"{font.Id} {style}");
        }

        // Every outline fixture in the folder is in the matrix (the CBDT emoji font has no outlines).
        var fixtures = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "Fixtures"), f))
            .Where(f => !f.Equals("NotoColorEmoji.ttf", StringComparison.OrdinalIgnoreCase));
        fixtures.ShouldBeSubsetOf(GoldenCatalog.Fonts.Select(f => f.FileName));
    }

    [Fact]
    public void ImageDiff_ReportsTheLargestLevelDifference()
    {
        // Guards the failure report: a one-level change on one pixel must be found and drawn.
        var expected = new GoldenImage(3, 2, 1, [0, 10, 20, 30, 40, 50]);
        var actual = new GoldenImage(3, 2, 1, [0, 10, 21, 30, 40, 50]);

        var (maxDiff, differing, diff) = GoldenImage.Compare(expected, actual);

        maxDiff.ShouldBe(1);
        differing.ShouldBe(1);
        diff.Pixels.AsSpan(2 * 4, 4).ToArray().ShouldBe(new byte[] { 132, 0, 132, 255 });
        GoldenPng.Decode(GoldenPng.Encode(actual)).Pixels.ShouldBe(actual.Pixels);
    }

    private static void AssertGenerateMatches(string id, GenerateCase goldenCase, GenerateGoldenResult result, string input)
    {
        var expected = GoldenStore.Get(id);
        if (expected is null)
            Assert.Fail($"{id}: no golden in the manifest. {GoldenStore.RegenerateHint}");

        string fntPath = GoldenStore.FntPath(id);
        if (!File.Exists(fntPath))
            Assert.Fail($"{id}: golden .fnt {fntPath} is missing. {GoldenStore.RegenerateHint}");

        string expectedFnt = GoldenRenderer.NormalizeFnt(File.ReadAllText(fntPath));
        var references = goldenCase.Reference
            ? Enumerable.Range(0, result.Pages.Count).Select(p => GoldenStore.ReadPng(PagePngPath(id, p))).ToList()
            : [];

        bool fntMatches = expectedFnt == result.Fnt;
        bool hashMatches = expected.Sha256 == result.Entry.Sha256;
        bool imagesMatch = references.Select((r, p) => r is not null && SamePixels(r, result.Pages[p])).All(m => m);
        if (fntMatches && hashMatches && imagesMatch)
            return;

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"BmFont.Generate output for golden '{id}' (input {input}) changed.\n");
        AppendChangedParts(report, expected.Parts, result.Entry.Parts, glyphs: null);

        string dir = GoldenStore.FailureDirectory(id + "/" + Path.GetFileName(input));
        File.WriteAllText(Path.Combine(dir, "actual.fnt"), result.Fnt);
        File.WriteAllText(Path.Combine(dir, "expected.fnt"), expectedFnt);
        if (!fntMatches)
            report.Append(FirstDifferentLine(expectedFnt, result.Fnt));

        for (int p = 0; p < result.Pages.Count; p++)
        {
            File.WriteAllBytes(Path.Combine(dir, $"actual-page{p}.png"), GoldenPng.Encode(result.Pages[p]));
            if (p < references.Count && references[p] is { } reference)
                AppendImageDiff(report, dir, $"-page{p}", reference, result.Pages[p]);
        }

        report.Append(CultureInfo.InvariantCulture, $"Artifacts: {dir}\n{GoldenStore.RegenerateHint}");
        Assert.Fail(report.ToString());
    }

    private static string PagePngPath(string id, int page) =>
        Path.Combine(GoldenStore.Directory, "png", GoldenCatalog.FileName(id) + $"-page{page}.png");

    /// <summary>Golden files under <paramref name="root"/>, as "png/name.png" / "fnt/name.fnt".</summary>
    private static IEnumerable<string> GoldenFiles(string root)
    {
        foreach (string folder in new[] { "png", "fnt" })
        {
            string dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.EnumerateFiles(dir))
                yield return folder + "/" + Path.GetFileName(file);
        }
    }

    private static bool SamePixels(GoldenImage a, GoldenImage b) =>
        a.Width == b.Width && a.Height == b.Height && a.Channels == b.Channels && a.Pixels.AsSpan().SequenceEqual(b.Pixels);

    private static void AppendChangedParts(StringBuilder report, string expectedParts, string actualParts, IReadOnlyList<GoldenGlyph>? glyphs)
    {
        static Dictionary<string, string> Split(string parts) =>
            parts.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);

        var expected = Split(expectedParts);
        var actual = Split(actualParts);
        var changed = actual.Where(p => !expected.TryGetValue(p.Key, out var e) || e != p.Value).Select(p => p.Key).ToList();
        var removed = expected.Keys.Where(k => !actual.ContainsKey(k)).ToList();

        report.Append(CultureInfo.InvariantCulture, $"{changed.Count} of {actual.Count} parts changed");
        report.Append(removed.Count > 0 ? $", {removed.Count} removed ({string.Join(", ", removed)}):\n" : ":\n");
        foreach (string key in changed)
        {
            var glyph = glyphs?.FirstOrDefault(g => g.Label == key);
            report.Append("  ").Append(glyph?.Describe() ?? key).Append('\n');
        }
    }

    private static void AppendImageDiff(StringBuilder report, string dir, string suffix, GoldenImage expected, GoldenImage actual)
    {
        var (maxDiff, differing, diff) = GoldenImage.Compare(expected, actual);
        File.WriteAllBytes(Path.Combine(dir, $"expected{suffix}.png"), GoldenPng.Encode(expected));
        File.WriteAllBytes(Path.Combine(dir, $"diff{suffix}.png"), GoldenPng.Encode(diff));
        report.Append(CultureInfo.InvariantCulture,
            $"Reference image{suffix}: max level diff {maxDiff}, {differing} pixels differ " +
            $"(expected {expected.Width}x{expected.Height}x{expected.Channels}, actual {actual.Width}x{actual.Height}x{actual.Channels}).\n");
    }

    private static string FirstDifferentLine(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            string el = i < e.Length ? e[i] : "<end>";
            string al = i < a.Length ? a[i] : "<end>";
            if (el != al)
                return $".fnt differs first at line {i + 1}:\n  expected: {el}\n  actual:   {al}\n";
        }

        return string.Empty;
    }
}
