using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Holds <see cref="TtfFontReader"/>'s merged <see cref="FontInfo.KerningPairs"/> (kern then
/// GPOS, later wins, first-insertion order) to a frozen copy of the original tuple-keyed
/// dictionary merge, for every fixture font with and without subsetting.
/// </summary>
public sealed class KerningMergeEquivalenceTests
{
    private static readonly string FixtureDir = Path.Join(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> FixtureFonts() => GposEquivalenceTests.FixtureFonts();

    [Theory]
    [MemberData(nameof(FixtureFonts))]
    public void FixtureFont_MergedKerningPairs_MatchReference(string relativePath)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, relativePath));
        var requests = new List<HashSet<int>?>
        {
            null,
            new(Enumerable.Range(0x20, 0x5F)),
            new(Enumerable.Range(0x20, 0xE0)),
            new(Enumerable.Range(0x20, 0x230)),
        };

        foreach (var requested in requests)
        {
            var info = new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font);
            var parser = new TtfParser(font, 0, requested);
            info.KerningPairs.ShouldBe(ReferenceMerge(parser), $"{relativePath} subset={requested?.Count}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void KernTableOverlappingGpos_MatchesReference(int seed)
    {
        // Roboto has no 'kern' table; add one holding some of its GPOS pairs (other values),
        // repeats, and unrelated pairs, so both the overwrite and the insertion order are exercised.
        var rng = new Random(seed);
        var roboto = File.ReadAllBytes(Path.Join(FixtureDir, "Roboto-Regular.ttf"));
        var gpos = new TtfParser(roboto, 0, new HashSet<int>(Enumerable.Range(0x20, 0xE0))).GposPairs;
        var kern = new List<(int Left, int Right, int Value)>();
        for (var i = 0; i < 400; i++)
        {
            var value = rng.Next(-90, 90);
            if (rng.Next(3) == 0)
                kern.Add((rng.Next(0, 300), rng.Next(0, 300), value));
            else
            {
                var g = gpos[rng.Next(gpos.Count)];
                kern.Add((g.LeftCodepoint, g.RightCodepoint, value));
            }
        }

        var font = GposEquivalenceTests.WithTable(roboto, "kern", KernTable(kern));
        foreach (var requested in new HashSet<int>?[] { null, new(Enumerable.Range(0x20, 0x5F)), new(Enumerable.Range(0x20, 0xE0)) })
        {
            var parser = new TtfParser(font, 0, requested);
            parser.KernPairs.ShouldNotBeEmpty();
            new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font).KerningPairs
                .ShouldBe(ReferenceMerge(parser));
        }
    }

    private static byte[] KernTable(List<(int Left, int Right, int Value)> pairs)
    {
        var bytes = new List<byte>();
        void U16(int v) { bytes.Add((byte)(v >> 8)); bytes.Add((byte)v); }
        U16(0); U16(1);                         // version 0, one subtable
        U16(0); U16(14 + pairs.Count * 6); U16(0x0001); // subtable version, length, coverage (format 0, horizontal)
        U16(pairs.Count); U16(0); U16(0); U16(0);
        foreach (var (l, r, v) in pairs)
        {
            U16(l); U16(r); U16((ushort)(short)v);
        }
        return bytes.ToArray();
    }

    /// <summary>The original merge, verbatim, over the resolved GPOS pairs the reader merges.</summary>
    private static List<KerningPair> ReferenceMerge(TtfParser parser)
    {
        var glyphToCodepoint = new Dictionary<int, int>();
        foreach (var (codepoint, glyphIndex) in parser.CmapTable)
        {
            if (glyphIndex == 0)
                continue;
            if (!glyphToCodepoint.TryGetValue(glyphIndex, out var existing) || codepoint < existing)
                glyphToCodepoint[glyphIndex] = codepoint;
        }

        var mergedPairs = new Dictionary<(int left, int right), int>();
        foreach (var pair in parser.KernPairs)
        {
            if (glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) &&
                glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
                mergedPairs[(leftCp, rightCp)] = pair.XAdvanceAdjustment;
        }

        foreach (var pair in parser.GposKerningPairs)
        {
            if (glyphToCodepoint.TryGetValue(pair.LeftCodepoint, out var leftCp) &&
                glyphToCodepoint.TryGetValue(pair.RightCodepoint, out var rightCp))
                mergedPairs[(leftCp, rightCp)] = pair.XAdvanceAdjustment;
        }

        return mergedPairs.Select(kvp => new KerningPair(kvp.Key.left, kvp.Key.right, kvp.Value)).ToList();
    }
}
