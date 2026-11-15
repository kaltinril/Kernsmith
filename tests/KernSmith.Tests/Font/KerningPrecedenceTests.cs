using KernSmith.Font;
using KernSmith.Font.Models;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// A font with GPOS pair kerning is kerned by GPOS alone, as a shaper does: its legacy
/// <c>kern</c> table is ignored, and is used only when GPOS has no pair kerning (#312).
/// </summary>
public sealed class KerningPrecedenceTests
{
    private static readonly byte[] Roboto =
        File.ReadAllBytes(Path.Join(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void KernTablePair_IsIgnored_WhenGposHasPairKerning()
    {
        var gposOnly = new TtfFontReader().ReadFont(Roboto).KerningPairs;
        var (left, right) = UnkernedPair(gposOnly);
        var shared = gposOnly.First(p => p.LeftCodepoint == 'T' && p.RightCodepoint == 'o');

        var font = WithKernTable((left, right, -50), ('T', 'o', 77));
        var pairs = new TtfFontReader().ReadFont(font).KerningPairs;

        pairs.Where(p => p.LeftCodepoint == left && p.RightCodepoint == right)
            .Select(p => p.XAdvanceAdjustment).ShouldBeEmpty();
        pairs.First(p => p.LeftCodepoint == 'T' && p.RightCodepoint == 'o').XAdvanceAdjustment
            .ShouldBe(shared.XAdvanceAdjustment);
        pairs.ShouldBe(gposOnly, ignoreOrder: true);
    }

    [Fact]
    public void PairOrder_StaysKernTableFirst_WhenGposHasPairKerning()
    {
        // Before #312 the kern table was merged first, so its pairs led the list. A font whose
        // kern pairs GPOS also carries (Arial) must keep writing the same .fnt, byte for byte.
        var gposOnly = new TtfFontReader().ReadFont(Roboto).KerningPairs;
        var (left, right) = UnkernedPair(gposOnly);
        var last = gposOnly[^1];
        var middle = gposOnly[gposOnly.Count / 2];

        var font = WithKernTable(
            (last.LeftCodepoint, last.RightCodepoint, 77),
            (left, right, -50),
            (middle.LeftCodepoint, middle.RightCodepoint, 77));
        var pairs = new TtfFontReader().ReadFont(font).KerningPairs;

        var expected = new List<KerningPair> { last, middle };
        expected.AddRange(gposOnly.Where(p => p != last && p != middle));
        pairs.ShouldBe(expected);
    }

    [Fact]
    public void KernTablePair_IsIgnored_WhenGposKernsNothingInTheSubset()
    {
        // The subset must not decide which table kerns: GPOS kerns this font, just not this pair.
        var (left, right) = UnkernedPair(new TtfFontReader().ReadFont(Roboto).KerningPairs);
        var font = WithKernTable((left, right, -50));

        var reader = new TtfFontReader { RequestedCodepoints = [left, right] };
        reader.ReadFont(font).KerningPairs.Select(p => p.XAdvanceAdjustment).ShouldBeEmpty();
    }

    [Fact]
    public void KernTable_IsTheFallback_WhenGposHasNoPairKerning()
    {
        // A GPOS with no features at all: header only, empty script, feature and lookup lists.
        byte[] emptyGpos = [0, 1, 0, 0, 0, 10, 0, 12, 0, 14, 0, 0, 0, 0, 0, 0];
        var font = GposEquivalenceTests.WithTable(WithKernTable(('T', 'o', 77)), "GPOS", emptyGpos);

        new TtfFontReader().ReadFont(font).KerningPairs
            .ShouldBe([new KerningPair('T', 'o', 77)]);
    }

    /// <summary>Roboto plus a <c>kern</c> table holding the given codepoint pairs.</summary>
    private static byte[] WithKernTable(params (int Left, int Right, int Value)[] pairs)
    {
        var cmap = new TtfParser(Roboto, 0, null, parseAdvancedTables: false).CmapTable;
        var glyphPairs = pairs.Select(p => (cmap[p.Left], cmap[p.Right], p.Value)).ToList();
        return GposEquivalenceTests.WithTable(Roboto, "kern", KerningMergeEquivalenceTests.KernTable(glyphPairs));
    }

    /// <summary>Two digits GPOS does not kern against each other.</summary>
    private static (int Left, int Right) UnkernedPair(IReadOnlyList<KerningPair> gposPairs)
    {
        var kerned = gposPairs.Select(p => (p.LeftCodepoint, p.RightCodepoint)).ToHashSet();
        for (var l = '0'; l <= '9'; l++)
        {
            for (var r = '0'; r <= '9'; r++)
            {
                if (!kerned.Contains((l, r)))
                    return (l, r);
            }
        }

        throw new InvalidOperationException("Roboto kerns every digit pair; pick another range.");
    }
}
