using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The <c>gasp</c> parser: ppem ranges with their grid-fitting and anti-aliasing flags,
/// hardened like the other table parsers — a malformed table is ignored (null), never an
/// exception.
/// </summary>
public class GaspTableTests
{
    /// <summary>The Microsoft shape: no grid-fit to 8, bilevel grid-fit to 16, everything above.</summary>
    private static byte[] Synthetic(params (ushort MaxPpem, ushort Behavior)[] ranges) => Synthetic(ranges.Length, ranges);

    private static byte[] Synthetic(int declaredRanges, (ushort MaxPpem, ushort Behavior)[] ranges)
    {
        var data = new byte[4 + (4 * ranges.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(data, 1);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)declaredRanges);
        for (int i = 0; i < ranges.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4 + (4 * i)), ranges[i].MaxPpem);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6 + (4 * i)), ranges[i].Behavior);
        }

        return data;
    }

    private static readonly (ushort, ushort)[] Microsoft = [(8, 0x000A), (16, 0x0005), (0xFFFF, 0x000F)];

    [Fact]
    public void Synthetic_ResolvesTheCoveringRange()
    {
        var gasp = GaspTable.TryParse(Synthetic(Microsoft));

        gasp.ShouldNotBeNull();
        gasp.Ranges.Count.ShouldBe(3);
        gasp.BehaviorAt(1).ShouldBe(GaspBehavior.DoGray | GaspBehavior.SymmetricSmoothing);
        gasp.BehaviorAt(8).ShouldBe(GaspBehavior.DoGray | GaspBehavior.SymmetricSmoothing);
        gasp.BehaviorAt(9).ShouldBe(GaspBehavior.GridFit | GaspBehavior.SymmetricGridFit);
        gasp.BehaviorAt(16).ShouldBe(GaspBehavior.GridFit | GaspBehavior.SymmetricGridFit);
        gasp.BehaviorAt(17).ShouldBe(GaspBehavior.GridFit | GaspBehavior.DoGray | GaspBehavior.SymmetricGridFit | GaspBehavior.SymmetricSmoothing);
        gasp.BehaviorAt(1000).ShouldBe(GaspBehavior.GridFit | GaspBehavior.DoGray | GaspBehavior.SymmetricGridFit | GaspBehavior.SymmetricSmoothing);

        gasp.GridFitsAt(8).ShouldBe(false);
        gasp.GridFitsAt(9).ShouldBe(true);
        gasp.GridFitsAt(40).ShouldBe(true);
    }

    [Fact]
    public void UnsortedRanges_StillResolveBySmallestCoveringMaximum()
    {
        var gasp = GaspTable.TryParse(Synthetic((0xFFFF, 0x000F), (16, 0x0005), (8, 0x000A)))!;

        gasp.GridFitsAt(8).ShouldBe(false);
        gasp.GridFitsAt(12).ShouldBe(true);
        gasp.BehaviorAt(12).ShouldBe(GaspBehavior.GridFit | GaspBehavior.SymmetricGridFit);
    }

    [Fact]
    public void SymmetricGridFitAlone_CountsAsGridFitting()
    {
        GaspTable.TryParse(Synthetic((0xFFFF, 0x0004)))!.GridFitsAt(10).ShouldBe(true);
        GaspTable.TryParse(Synthetic((0xFFFF, 0x0008)))!.GridFitsAt(10).ShouldBe(false);
        GaspTable.TryParse(Synthetic((0xFFFF, 0x0000)))!.GridFitsAt(10).ShouldBe(false);
    }

    [Fact]
    public void WithoutTheSentinel_AnUncoveredPpemIsUnknown()
    {
        var gasp = GaspTable.TryParse(Synthetic((8, 0x000A), (16, 0x0005)))!;

        gasp.GridFitsAt(16).ShouldBe(true);
        gasp.GridFitsAt(17).ShouldBeNull();
        gasp.BehaviorAt(17).ShouldBeNull();
    }

    [Fact]
    public void Malformed_IsIgnored()
    {
        GaspTable.TryParse([]).ShouldBeNull("empty");
        GaspTable.TryParse(new byte[3]).ShouldBeNull("shorter than the header");
        GaspTable.TryParse(Synthetic(0, [])).ShouldBeNull("no ranges");
        GaspTable.TryParse(Synthetic(4, Microsoft)).ShouldBeNull("declares more ranges than it holds");
        GaspTable.TryParse(Synthetic(Microsoft).AsSpan(0, 10)).ShouldBeNull("truncated inside a range");
        GaspTable.TryParse(Synthetic(2, Microsoft)).ShouldNotBeNull("extra bytes after the declared ranges are allowed");
    }

    public static TheoryData<string> FixturesWithGasp => ["NotoSans-Regular-Latin.ttf", "DancingScript-Variable.ttf", "RobotoFlex-Variable.ttf"];

    [Theory]
    [MemberData(nameof(FixturesWithGasp))]
    public void Fixture_GridFitsEverywhere(string fixture)
    {
        // These fixtures ship the one-range table font tools write: every size, every flag.
        var face = NativeFontFace.Load(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, fixture)));
        var gasp = GaspTable.TryParse(face.Tables.GetTable("gasp").Span);

        gasp.ShouldNotBeNull();
        gasp.Ranges.ShouldBe([new GaspRange(0xFFFF, GaspBehavior.GridFit | GaspBehavior.DoGray | GaspBehavior.SymmetricGridFit | GaspBehavior.SymmetricSmoothing)]);
        gasp.GridFitsAt(6).ShouldBe(true);
    }

    [Fact]
    public void Roboto_HasNoGasp()
    {
        NativeFontFace.Load(TestFonts.RobotoRegularBytes()).Tables.HasTable("gasp").ShouldBeFalse();
    }

    [Fact]
    public void Verdana_AsksForNoGridFitUpToEightPpem()
    {
        var face = NativeFontFace.Load(NativeBytecodeFontTests.SystemFont("verdana.ttf"));
        var gasp = GaspTable.TryParse(face.Tables.GetTable("gasp").Span)!;

        gasp.Ranges[0].ShouldBe(new GaspRange(8, GaspBehavior.DoGray | GaspBehavior.SymmetricSmoothing));
        gasp.GridFitsAt(8).ShouldBe(false);
        gasp.GridFitsAt(9).ShouldBe(true);
        gasp.GridFitsAt(16).ShouldBe(true);
        gasp.GridFitsAt(72).ShouldBe(true);
    }

    [Fact]
    public void Damaged_NeverThrows()
    {
        var tables = new List<(string Name, byte[] Bytes)> { ("synthetic", Synthetic(Microsoft)) };
        foreach (string fixture in new[] { "NotoSans-Regular-Latin.ttf", "DancingScript-Variable.ttf" })
            tables.Add((fixture, NativeFontFace.Load(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(TestFonts.RobotoRegularPath)!, fixture))).Tables.GetTable("gasp").ToArray()));
        var verdana = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "verdana.ttf");
        if (File.Exists(verdana))
            tables.Add(("verdana", NativeFontFace.Load(File.ReadAllBytes(verdana)).Tables.GetTable("gasp").ToArray()));

        foreach (var (name, bytes) in tables)
        {
            for (int length = 0; length <= bytes.Length; length++)
                Should.NotThrow(() => Probe(GaspTable.TryParse(bytes.AsSpan(0, length))), $"{name} truncated to {length}");

            var rng = new Random(0x6761);
            for (int i = 0; i < 200; i++)
            {
                var damaged = (byte[])bytes.Clone();
                damaged[rng.Next(damaged.Length)] ^= (byte)(1 << rng.Next(8));
                if (i % 3 == 0)
                    rng.NextBytes(damaged.AsSpan(rng.Next(damaged.Length)));
                Should.NotThrow(() => Probe(GaspTable.TryParse(damaged)), $"{name} damaged, case {i}");
            }
        }
    }

    private static void Probe(GaspTable? gasp)
    {
        if (gasp is null)
            return;

        foreach (int ppem in new[] { 0, 1, 8, 9, 16, 17, 40, 0xFFFF, 0x10000 })
            gasp.GridFitsAt(ppem);
    }
}
