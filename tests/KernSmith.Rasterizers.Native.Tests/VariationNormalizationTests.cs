using KernSmith.Font.Tables;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 — user axis values to normalized coordinates: the default <c>fvar</c> mapping,
/// clamping, F2DOT14 quantization and <c>avar</c> v1 segment maps, per the OpenType Font
/// Variations overview ("Coordinate scales and normalization").
/// </summary>
public class VariationNormalizationTests
{
    private static readonly VariationAxis Weight = new("wght", 100f, 400f, 1000f, "Weight");
    private static readonly VariationAxis Width = new("wdth", 25f, 100f, 151f, "Width");

    [Theory]
    [InlineData(400f, 0f)]
    [InlineData(100f, -1f)]
    [InlineData(1000f, 1f)]
    [InlineData(250f, -0.5f)]
    [InlineData(700f, 0.5f)]
    [InlineData(-5000f, -1f)]   // clamped to the axis minimum
    [InlineData(5000f, 1f)]     // clamped to the axis maximum
    public void DefaultMapping_IsPiecewiseLinearAroundTheDefault(float user, float expected)
    {
        var coords = AxisNormalizer.Normalize([Weight], new Dictionary<string, float> { ["wght"] = user }, avar: null);

        coords.ShouldBe([expected]);
    }

    [Fact]
    public void Result_IsQuantizedToF2Dot14()
    {
        // 401 on a 400..1000 axis is 1/600 = 0.0016667. In 16.16 that is 109.23 -> 109, and the
        // spec's 16.16 -> 2.14 conversion ((v + 2) >> 2) gives 27, i.e. 27/16384.
        var coords = AxisNormalizer.Normalize([Weight], new Dictionary<string, float> { ["wght"] = 401f }, avar: null);

        coords[0].ShouldBe(27f / 16384f);
    }

    [Fact]
    public void UnspecifiedAndUnknownAxes_StayAtTheDefault()
    {
        var coords = AxisNormalizer.Normalize(
            [Weight, Width],
            new Dictionary<string, float> { ["wdth"] = 151f, ["ZZZZ"] = 3f },
            avar: null);

        coords.ShouldBe([0f, 1f]);
    }

    /// <summary>The 'avar' normalization example from the OpenType overview chapter.</summary>
    [Theory]
    [InlineData(100f, -1.0f)]
    [InlineData(175f, -0.5f)]       // -0.75 -> -0.5 (a map point)
    [InlineData(250f, -0.3333f)]    // -0.5 -> -0.3333
    [InlineData(325f, -0.1667f)]    // -0.25 -> -0.1667
    [InlineData(400f, 0f)]
    [InlineData(525f, 0.25f)]       // 0.25 -> 0.25
    [InlineData(650f, 0.65f)]       // 0.5 -> 0.65
    [InlineData(775f, 0.9375f)]     // 0.75 -> 0.9375
    [InlineData(900f, 1.0f)]
    public void Avar_SegmentMaps_MatchTheSpecExample(float user, float expected)
    {
        var axis = new VariationAxis("wght", 100f, 400f, 900f, null);
        var avar = AvarTable.Parse(BuildAvar(
            [(-1.0, -1.0), (-0.75, -0.5), (0, 0), (0.4, 0.4), (0.6, 0.9), (1.0, 1.0)]));

        var coords = AxisNormalizer.Normalize([axis], new Dictionary<string, float> { ["wght"] = user }, avar);

        // One F2DOT14 step is 1/16384 = 0.00006; the spec's table rounds to four places.
        coords[0].ShouldBe(expected, 0.0001f);
    }

    [Fact]
    public void Avar_WithoutTheRequiredZeroMapping_LeavesTheAxisUnmodified()
    {
        // A segment map must contain -1->-1, 0->0 and 1->1; without them it is ignored.
        var avar = AvarTable.Parse(BuildAvar([(-1.0, -1.0), (0.5, 0.9), (1.0, 1.0)]));

        var coords = AxisNormalizer.Normalize([Weight], new Dictionary<string, float> { ["wght"] = 700f }, avar);

        coords[0].ShouldBe(0.5f);
    }

    [Fact]
    public void Avar_AxisWithNoMaps_IsIdentity()
    {
        var avar = AvarTable.Parse(BuildAvar([]));

        var coords = AxisNormalizer.Normalize([Weight], new Dictionary<string, float> { ["wght"] = 700f }, avar);

        coords[0].ShouldBe(0.5f);
    }

    /// <summary>A one-axis <c>avar</c> v1 table with the given (from, to) maps.</summary>
    internal static byte[] BuildAvar((double From, double To)[] maps)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Be.U16(1));  // majorVersion
        bytes.AddRange(Be.U16(0));  // minorVersion
        bytes.AddRange(Be.U16(0));  // reserved
        bytes.AddRange(Be.U16(1));  // axisCount
        bytes.AddRange(Be.U16((ushort)maps.Length));
        foreach (var (from, to) in maps)
        {
            bytes.AddRange(Be.F2Dot14(from));
            bytes.AddRange(Be.F2Dot14(to));
        }

        return bytes.ToArray();
    }
}
