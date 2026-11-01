using System.Buffers.Binary;
using KernSmith.Font;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// How GPOS PairPos values combine into one kerning value per pair (#268): within a lookup the
/// first subtable that matches the pair wins (a format 1 record matches its pair, explicit zero
/// included; a format 2 subtable matches every pair whose first glyph it covers), and the
/// lookups the 'kern' feature lists are each applied once and add up.
/// </summary>
public sealed class GposKerningResolutionTests
{
    private static readonly string FixtureDir = Path.Join(AppContext.BaseDirectory, "Fixtures");

    // Values read independently with fontTools (font units).
    [Theory]
    [InlineData("SourceSerif4-Regular.otf", 'V', 'ä', -60)]
    [InlineData("SourceSerif4-Regular.otf", 'Y', 'î', -20)]
    [InlineData("SourceSerif4-Regular.otf", 'f', 'ì', 60)]
    [InlineData("DancingScript-Variable.ttf", 'Ç', 'a', -68)]
    [InlineData("DancingScript-Variable.ttf", 'Ç', 'E', 1)]
    public void FixtureFont_SpecificPairBeatsLaterClassPair(string file, char left, char right, int expected)
    {
        var font = File.ReadAllBytes(Path.Join(FixtureDir, file));
        foreach (var requested in new HashSet<int>?[] { null, new(Enumerable.Range(0x20, 0x230)) })
        {
            var info = new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font);
            Kerning(info, left, right).ShouldBe(expected, $"{file} '{left}{right}' subset={requested?.Count}");
        }
    }

    [Fact]
    public void ExplicitZeroPairCancelsLaterClassValue()
    {
        var (font, a, v) = RobotoWithGpos((a, v) => Gpos(
            [0], new[] { Format1(a, (v, 0)), Format2(a, v, -50) }));

        Kerning(new TtfFontReader().ReadFont(font), 'A', 'V').ShouldBe(0);
    }

    [Fact]
    public void ClassSubtableShadowsLaterSpecificPair()
    {
        // The class subtable comes first and covers A, so it wins even for a pair a later
        // format 1 subtable lists.
        var (font, _, _) = RobotoWithGpos((a, v) => Gpos(
            [0], new[] { Format2(a, v, -50), Format1(a, (v, -10)) }));

        Kerning(new TtfFontReader().ReadFont(font), 'A', 'V').ShouldBe(-50);
    }

    [Fact]
    public void SpecificPairWithoutTheSecondGlyphDoesNotShadow()
    {
        // A format 1 subtable covering A but with no record for V does not match A V, so the
        // class value still applies.
        var (font, _, _) = RobotoWithGpos((a, v) => Gpos(
            [0], new[] { Format1(a, (a, -5)), Format2(a, v, -50) }));

        var info = new TtfFontReader().ReadFont(font);
        Kerning(info, 'A', 'V').ShouldBe(-50);
        Kerning(info, 'A', 'A').ShouldBe(-5);
    }

    [Fact]
    public void LookupsAddUpAndEachAppliesOnce()
    {
        // The feature lists lookup 1 before 0 and lookup 0 twice; each applies once, and the
        // two values add.
        var (font, _, _) = RobotoWithGpos((a, v) => Gpos(
            [1, 0, 0], new[] { Format1(a, (v, -20)) }, new[] { Format1(a, (v, -30)) }));

        Kerning(new TtfFontReader().ReadFont(font), 'A', 'V').ShouldBe(-50);
    }

    [Fact]
    public void ClassZeroSecondGlyphsAreKernedWithAndWithoutASubset()
    {
        // ClassDef2 lists only V (class 1); every other second glyph is in class 0, kerned -30
        // after A. Without a subset the parser used to drop class 0 (#277).
        var (font, _, _) = RobotoWithGpos((a, v) => Gpos([0], new[] { Format2(a, v, -50, classZeroValue: -30) }));

        foreach (var requested in new HashSet<int>?[] { null, new() { 'A', 'V', 'B', 'o' } })
        {
            var info = new TtfFontReader { RequestedCodepoints = requested }.ReadFont(font);
            Kerning(info, 'A', 'V').ShouldBe(-50, $"subset={requested?.Count}");
            Kerning(info, 'A', 'B').ShouldBe(-30, $"subset={requested?.Count}");
            Kerning(info, 'A', 'o').ShouldBe(-30, $"subset={requested?.Count}");
            Kerning(info, 'V', 'A').ShouldBe(0, $"subset={requested?.Count}");
        }
    }

    // ------------------------------------------------------------ helpers

    private static int Kerning(KernSmith.Font.Models.FontInfo info, char left, char right) =>
        info.KerningPairs
            .Where(p => p.LeftCodepoint == left && p.RightCodepoint == right)
            .Select(p => p.XAdvanceAdjustment)
            .DefaultIfEmpty(0)
            .Single();

    private static (byte[] Font, int A, int V) RobotoWithGpos(Func<int, int, byte[]> gpos)
    {
        var roboto = File.ReadAllBytes(Path.Join(FixtureDir, "Roboto-Regular.ttf"));
        var cmap = new TtfParser(roboto, 0, null, parseAdvancedTables: false).CmapTable;
        int a = cmap['A'], v = cmap['V'];
        return (GposEquivalenceTests.WithTable(roboto, "GPOS", gpos(a, v)), a, v);
    }

    /// <summary>A GPOS whose one 'kern' feature lists <paramref name="featureLookups"/>, over PairPos lookups.</summary>
    private static byte[] Gpos(int[] featureLookups, params byte[][][] lookups) => BuildGpos(featureLookups, lookups);

    private static byte[] BuildGpos(int[] featureLookups, byte[][][] lookups)
    {
        // header(10) | FeatureList: count(2) + record(6) + Feature(4 + 2n) | LookupList
        var feature = Concat(U16(0), U16(featureLookups.Length), Concat(featureLookups.Select(U16).ToArray()));
        var featureList = Concat(U16(1), "kern"u8.ToArray(), U16(8), feature);

        var lookupTables = lookups.Select(subtables =>
        {
            var header = 6 + subtables.Length * 2;
            var offsets = new List<byte[]>();
            var cursor = header;
            foreach (var s in subtables)
            {
                offsets.Add(U16(cursor));
                cursor += s.Length;
            }
            return Concat(U16(2), U16(0), U16(subtables.Length), Concat(offsets.ToArray()), Concat(subtables));
        }).ToArray();

        var lookupOffsets = new List<byte[]>();
        var at = 2 + lookupTables.Length * 2;
        foreach (var l in lookupTables)
        {
            lookupOffsets.Add(U16(at));
            at += l.Length;
        }
        var lookupList = Concat(U16(lookupTables.Length), Concat(lookupOffsets.ToArray()), Concat(lookupTables));

        return Concat(U16(1), U16(0), U16(0), U16(10), U16(10 + featureList.Length), featureList, lookupList);
    }

    /// <summary>PairPos format 1: one pair set for <paramref name="first"/>, XAdvance values.</summary>
    private static byte[] Format1(int first, params (int Second, int Value)[] records)
    {
        var pairSet = Concat(U16(records.Length),
            Concat(records.Select(r => Concat(U16(r.Second), I16(r.Value))).ToArray()));
        var coverageAt = 12 + pairSet.Length;
        return Concat(U16(1), U16(coverageAt), U16(0x0004), U16(0), U16(1), U16(12),
            pairSet, U16(1), U16(1), U16(first));
    }

    /// <summary>
    /// PairPos format 2 covering <paramref name="first"/> (class 0), with <paramref name="second"/>
    /// in class 1 kerned by <paramref name="value"/> and class 0 by <paramref name="classZeroValue"/>.
    /// </summary>
    private static byte[] Format2(int first, int second, int value, int classZeroValue = 0)
    {
        const int coverageAt = 16 + 4;
        const int classDef1At = coverageAt + 6;
        const int classDef2At = classDef1At + 4;
        return Concat(U16(2), U16(coverageAt), U16(0x0004), U16(0),
            U16(classDef1At), U16(classDef2At), U16(1), U16(2),
            I16(classZeroValue), I16(value),
            U16(1), U16(1), U16(first),
            U16(2), U16(0),
            U16(1), U16(second), U16(1), U16(1));
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] U16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }

    private static byte[] I16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
        return buffer;
    }
}
