using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer.StbStructure;

/// <summary>
/// The structure check on damaged fonts: each fixture is mutated table by table (bit flips,
/// extreme 16- and 32-bit values, random and zeroed runs, directory offset and length changes,
/// truncation) and run through the validator alone — font level, then every glyph. The only
/// acceptable outcomes are a pass or a <see cref="FontParsingException"/>, and no case may take
/// long. stb_truetype is never called on the damaged bytes.
/// </summary>
/// <remarks>
/// Deterministic: case <c>n</c> of a fixture always applies the same mutation, so a failure
/// reproduces from its message. Set <c>KERNSMITH_STB_SWEEP_ITERATIONS</c> for a longer run.
/// </remarks>
public class FontStructureSweepTests
{
    private const int DefaultIterations = 400;

    private static readonly string[] Tables = ["cmap", "head", "hhea", "hmtx", "maxp", "loca", "glyf", "CFF "];

    public static TheoryData<string> Fonts() =>
        ["Roboto-Regular.ttf", "NotoSans-Regular-Latin.ttf", "SourceSerif4-Regular.otf", "NotoSans-Regular-Latin-CFF.otf", "synthetic CID"];

    [Theory]
    [MemberData(nameof(Fonts))]
    public void DamagedFont_PassesOrThrowsFontParsingException(string fixture)
    {
        byte[] original = fixture == "synthetic CID"
            ? SyntheticFonts.Cff(
                [SyntheticFonts.Square, "0 0 rmoveto -107 callsubr -106 callgsubr endchar", "10 20 hstemhm hintmask 0x0B 0 0 rmoveto 1 2 3 4 5 6 rrcurveto endchar"],
                globalSubrs: ["return", "0 50 rlineto return"],
                localSubrs: ["50 0 rlineto return"],
                cid: true)
            : SfntEditor.Fixture(fixture);
        var tables = Directory(original).Where(t => Tables.Contains(t.Tag)).ToList();
        int iterations = Iterations();
        var slowest = TimeSpan.Zero;
        int passed = 0;

        for (int i = 0; i < iterations; i++)
        {
            var rng = new Random(HashCode.Combine(fixture.Length, i, 0x5B7));
            byte[] damaged = (byte[])original.Clone();
            string what = Mutate(ref damaged, tables, rng);
            string caseName = $"{fixture} case {i}: {what}";

            var watch = Stopwatch.StartNew();
            try
            {
                var validator = FontStructureValidator.Validate(damaged, 0);
                for (int g = 0; g < validator.NumGlyphs; g++)
                {
                    try
                    {
                        validator.EnsureGlyph(g);
                    }
                    catch (FontParsingException)
                    {
                    }
                }

                passed++;
            }
            catch (FontParsingException)
            {
            }
            catch (Exception ex)
            {
                throw new ShouldAssertException($"{caseName} threw {ex.GetType().Name}: {ex.Message}", ex);
            }

            if (watch.Elapsed > slowest)
                slowest = watch.Elapsed;
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), caseName);
        }

        // Guards the mutator: some damage must be benign (a flipped bit in a coordinate), so a
        // sweep that rejected every case would mean the mutations never reached the checks.
        passed.ShouldBeGreaterThan(0);
        TestContext.Current.SendDiagnosticMessage($"{fixture}: {passed}/{iterations} damaged fonts pass; slowest case {slowest.TotalMilliseconds:0} ms.");
    }

    private static int Iterations() =>
        int.TryParse(Environment.GetEnvironmentVariable("KERNSMITH_STB_SWEEP_ITERATIONS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0
            ? n
            : DefaultIterations;

    private readonly record struct TableEntry(string Tag, int Record, int Offset, int Length);

    private static List<TableEntry> Directory(byte[] font)
    {
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var list = new List<TableEntry>();
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (16 * i);
            list.Add(new TableEntry(
                Encoding.ASCII.GetString(font, rec, 4), rec,
                (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 8)),
                (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(rec + 12))));
        }

        return list;
    }

    private static readonly ushort[] Extreme16 = [0, 1, 2, 0x7FFF, 0x8000, 0xFFFE, 0xFFFF];

    private static readonly uint[] Extreme32 = [0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x10000, 0xFFFF];

    /// <summary>Applies one mutation in place and describes it.</summary>
    private static string Mutate(ref byte[] font, List<TableEntry> tables, Random rng)
    {
        byte[] bytes = font;
        var t = tables[rng.Next(tables.Count)];
        int Pos(int width) => t.Offset + rng.Next(Math.Max(1, Math.Min(t.Length, bytes.Length - t.Offset) - width + 1));

        switch (rng.Next(9))
        {
            case 0:
                {
                    int flips = 1 + rng.Next(4);
                    for (int k = 0; k < flips; k++)
                        font[Pos(1)] ^= (byte)(1 << rng.Next(8));
                    return $"'{t.Tag}' {flips} bit flips";
                }

            case 1:
                {
                    int at = Pos(2) & ~1;
                    ushort v = Extreme16[rng.Next(Extreme16.Length)];
                    BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(at), v);
                    return $"'{t.Tag}' u16 at +{at - t.Offset} = 0x{v:X4}";
                }

            case 2:
                {
                    int at = Pos(4) & ~1;
                    uint v = Extreme32[rng.Next(Extreme32.Length)];
                    BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(at), v);
                    return $"'{t.Tag}' u32 at +{at - t.Offset} = 0x{v:X8}";
                }

            case 3:
                {
                    int at = Pos(16);
                    int n = 1 + rng.Next(Math.Min(16, font.Length - at));
                    rng.NextBytes(font.AsSpan(at, n));
                    return $"'{t.Tag}' {n} random bytes at +{at - t.Offset}";
                }

            case 4:
                {
                    int at = Pos(32);
                    int n = 1 + rng.Next(Math.Min(32, font.Length - at));
                    font.AsSpan(at, n).Clear();
                    return $"'{t.Tag}' {n} zero bytes at +{at - t.Offset}";
                }

            case 5:
                {
                    long length = rng.Next(3) switch
                    {
                        0 => rng.Next(Math.Max(1, t.Length)),
                        1 => t.Length + 1 + rng.Next(64),
                        _ => Extreme32[rng.Next(Extreme32.Length)],
                    };
                    BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(t.Record + 12), (uint)length);
                    return $"'{t.Tag}' directory length {t.Length} -> {(uint)length}";
                }

            case 6:
                {
                    uint offset = rng.Next(2) == 0 ? (uint)rng.Next(font.Length) : Extreme32[rng.Next(Extreme32.Length)];
                    BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(t.Record + 8), offset);
                    return $"'{t.Tag}' directory offset {t.Offset} -> {offset}";
                }

            case 7:
                {
                    // A structure field near the table start (headers, counts, first offsets).
                    int at = t.Offset + (rng.Next(Math.Max(1, Math.Min(64, t.Length) / 2)) * 2);
                    ushort v = Extreme16[rng.Next(Extreme16.Length)];
                    BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(at), v);
                    return $"'{t.Tag}' header u16 at +{at - t.Offset} = 0x{v:X4}";
                }

            default:
                {
                    int keep = rng.Next(font.Length);
                    font = font[..keep];
                    return $"file truncated to {keep} bytes";
                }
        }
    }
}
