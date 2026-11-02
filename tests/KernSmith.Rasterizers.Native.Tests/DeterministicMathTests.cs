using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// <see cref="DeterministicMath"/>: built from the IEEE-exact operations only, so its bits are
/// the same on every runtime, OS and CPU. The tests hold it (1) close to the libm answer —
/// within a few double ulps, and never more than one float ulp apart once narrowed — (2) exact
/// at the special values callers rely on, (3) monotonic, and (4) bit-for-bit at pinned inputs,
/// which is what CI on Windows, Linux, macOS, arm64 and the browser compares.
/// </summary>
public class DeterministicMathTests
{
    /// <summary>Relative tolerance against <c>System.Math</c>: about 20 double ulps, far below
    /// one float ulp (2^-24), with room for the libm's own last-bit variation.</summary>
    private const double Tolerance = 4.5e-15;

    private static readonly double[] Angles =
    [
        0, 1e-300, 1e-8, 0.1, 0.5, 0.75, 1, 1.5, Math.PI / 4, Math.PI / 2, 2, Math.PI, 4, 5, 2 * Math.PI, 7, 10, 100, 1000, 12345.678,
        -1e-8, -0.3, -1, -Math.PI / 2, -3, -2 * Math.PI, -50, -1e5,
        12 * Math.PI / 180, -12 * Math.PI / 180, 30 * Math.PI / 180, 45 * Math.PI / 180, 60 * Math.PI / 180,
    ];

    private static readonly double[] Positives =
    [
        1e-300, 2.2250738585072014e-308, 1e-10, 0.0031308, 0.04045, 0.09, 0.2, 0.5, 0.7071, 0.999999, 1, 1.000001, 1.5, 2, 2.4, Math.E, 3, 10, 255, 1e3, 1e10, 1e300,
    ];

    public static TheoryData<double> AngleData() => [.. Angles];

    public static TheoryData<double> PositiveData() => [.. Positives];

    // ---- Agreement with libm ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AngleData))]
    public void Sin_Cos_Tan_AgreeWithMath(double x)
    {
        Close(DeterministicMath.Sin(x), Math.Sin(x), $"sin {x}", absolute: 1e-15);
        Close(DeterministicMath.Cos(x), Math.Cos(x), $"cos {x}", absolute: 1e-15);
        // tan blows up near odd multiples of pi/2; hold the relative error where it is finite and modest.
        if (Math.Abs(Math.Cos(x)) > 1e-3)
            Close(DeterministicMath.Tan(x), Math.Tan(x), $"tan {x}", absolute: 1e-15);
    }

    [Theory]
    [MemberData(nameof(PositiveData))]
    public void Log_Exp_AgreeWithMath(double x)
    {
        Close(DeterministicMath.Log(x), Math.Log(x), $"log {x}", absolute: 1e-15);
        double e = Math.Min(x, 700);
        Close(DeterministicMath.Exp(e), Math.Exp(e), $"exp {e}");
        Close(DeterministicMath.Exp(-e), Math.Exp(-e), $"exp {-e}");
    }

    [Theory]
    [InlineData(0.5, 2.4)]
    [InlineData(0.09, 2.4)]
    [InlineData(0.999, 2.4)]
    [InlineData(0.25, 1 / 2.4)]
    [InlineData(0.0031309, 1 / 2.4)]
    [InlineData(2, 0.5)]
    [InlineData(2, 10)]
    [InlineData(10, -3)]
    [InlineData(1.5, 100)]
    [InlineData(0.5, -1000)]
    [InlineData(-2, 3)]
    [InlineData(-2, 4)]
    [InlineData(-0.5, -3)]
    public void Pow_AgreesWithMath(double x, double y)
    {
        // exp(y ln x) amplifies the logarithm's rounding by |y ln x|; the tolerance follows.
        double amplification = Math.Max(1, Math.Abs(y * Math.Log(Math.Abs(x))));
        Close(DeterministicMath.Pow(x, y), Math.Pow(x, y), $"pow {x} {y}", relative: Tolerance * amplification);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(0, -1)]
    [InlineData(-1, 0)]
    [InlineData(-1, -1)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(0.5, 2)]
    [InlineData(3, -0.25)]
    [InlineData(-1e-3, -1e3)]
    [InlineData(1e300, 1e-300)]
    [InlineData(-7, 1e-9)]
    [InlineData(0.70710678, -0.70710678)]
    public void Atan2_AgreesWithMath(double y, double x) => Close(DeterministicMath.Atan2(y, x), Math.Atan2(y, x), $"atan2 {y} {x}", absolute: 1e-15);

    [Theory]
    [InlineData(1)]
    [InlineData(0.999999)]
    [InlineData(0.5)]
    [InlineData(0.1)]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(-0.5)]
    [InlineData(-0.999999)]
    [InlineData(-1)]
    public void Acos_AgreesWithMath(double x) => Close(DeterministicMath.Acos(x), Math.Acos(x), $"acos {x}", absolute: 1e-15);

    [Fact]
    public void NarrowedToFloat_IsWithinOneUlpOfLibm()
    {
        // The call sites narrow to float (or further, to bytes); a double disagreement of a few
        // ulps can at most flip a float rounding that was already on the fence.
        foreach (double x in Angles)
        {
            WithinOneFloatUlp((float)DeterministicMath.Sin(x), (float)Math.Sin(x), $"sin {x}");
            WithinOneFloatUlp((float)DeterministicMath.Cos(x), (float)Math.Cos(x), $"cos {x}");
        }

        foreach (double x in Positives)
        {
            WithinOneFloatUlp((float)DeterministicMath.Log(x), (float)Math.Log(x), $"log {x}");
            WithinOneFloatUlp((float)DeterministicMath.Pow(x, 2.4), (float)Math.Pow(x, 2.4), $"pow {x} 2.4");
        }
    }

    // ---- Special values ----------------------------------------------------------------------

    [Fact]
    public void SpecialValues_AreExact()
    {
        DeterministicMath.Sin(0).ShouldBe(0.0);
        double.IsNegative(DeterministicMath.Sin(-0.0)).ShouldBeTrue();
        DeterministicMath.Cos(0).ShouldBe(1.0);
        DeterministicMath.Tan(0).ShouldBe(0.0);
        double.IsNaN(DeterministicMath.Sin(double.PositiveInfinity)).ShouldBeTrue();
        double.IsNaN(DeterministicMath.Cos(double.NaN)).ShouldBeTrue();

        DeterministicMath.Exp(0).ShouldBe(1.0);
        DeterministicMath.Exp(double.NegativeInfinity).ShouldBe(0.0);
        DeterministicMath.Exp(double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Exp(1000).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Exp(-1000).ShouldBe(0.0);
        double.IsNaN(DeterministicMath.Exp(double.NaN)).ShouldBeTrue();

        DeterministicMath.Log(1).ShouldBe(0.0);
        DeterministicMath.Log(0).ShouldBe(double.NegativeInfinity);
        DeterministicMath.Log(double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        double.IsNaN(DeterministicMath.Log(-1)).ShouldBeTrue();
        double.IsNaN(DeterministicMath.Log(double.NaN)).ShouldBeTrue();

        DeterministicMath.Pow(0, 0).ShouldBe(1.0);
        DeterministicMath.Pow(double.NaN, 0).ShouldBe(1.0);
        DeterministicMath.Pow(1, double.NaN).ShouldBe(1.0);
        DeterministicMath.Pow(2, 1).ShouldBe(2.0);
        DeterministicMath.Pow(0, 2.4).ShouldBe(0.0);
        DeterministicMath.Pow(0, -1).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Pow(-0.0, -3).ShouldBe(double.NegativeInfinity);
        DeterministicMath.Pow(-8, 1.0 / 3).ShouldBe(double.NaN);
        DeterministicMath.Pow(-2, 3).ShouldBe(-8.0);
        DeterministicMath.Pow(-2, 2).ShouldBe(4.0);
        DeterministicMath.Pow(2, 10).ShouldBe(1024.0);
        DeterministicMath.Pow(3, 5).ShouldBe(243.0);
        DeterministicMath.Pow(0.5, -1000).ShouldBe(BitConverter.Int64BitsToDouble((1000L + 1023) << 52));
        DeterministicMath.Pow(10, -3).ShouldBe(0.001);
        DeterministicMath.Pow(2, 2000).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Pow(2, -2000).ShouldBe(0.0);
        DeterministicMath.Pow(0.5, double.PositiveInfinity).ShouldBe(0.0);
        DeterministicMath.Pow(2, double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Pow(2, double.NegativeInfinity).ShouldBe(0.0);
        DeterministicMath.Pow(double.PositiveInfinity, 2).ShouldBe(double.PositiveInfinity);
        DeterministicMath.Pow(double.PositiveInfinity, -2).ShouldBe(0.0);
        DeterministicMath.Pow(double.NegativeInfinity, 3).ShouldBe(double.NegativeInfinity);
        DeterministicMath.Pow(double.NegativeInfinity, 2).ShouldBe(double.PositiveInfinity);
        double.IsNaN(DeterministicMath.Pow(double.NaN, 1)).ShouldBeTrue();

        DeterministicMath.Atan(0).ShouldBe(0.0);
        double.IsNegative(DeterministicMath.Atan(-0.0)).ShouldBeTrue();
        DeterministicMath.Atan(double.PositiveInfinity).ShouldBe(Math.PI / 2);
        DeterministicMath.Atan(double.NegativeInfinity).ShouldBe(-Math.PI / 2);

        DeterministicMath.Atan2(0, 1).ShouldBe(0.0);
        double.IsNegative(DeterministicMath.Atan2(-0.0, 1)).ShouldBeTrue();
        DeterministicMath.Atan2(0, -1).ShouldBe(Math.PI);
        DeterministicMath.Atan2(-0.0, -1).ShouldBe(-Math.PI);
        DeterministicMath.Atan2(1, 0).ShouldBe(Math.PI / 2);
        DeterministicMath.Atan2(-1, 0).ShouldBe(-Math.PI / 2);
        DeterministicMath.Atan2(0, 0).ShouldBe(0.0);
        DeterministicMath.Atan2(0, -0.0).ShouldBe(Math.PI);
        DeterministicMath.Atan2(1, double.PositiveInfinity).ShouldBe(0.0);
        DeterministicMath.Atan2(1, double.NegativeInfinity).ShouldBe(Math.PI);
        DeterministicMath.Atan2(double.PositiveInfinity, 1).ShouldBe(Math.PI / 2);
        DeterministicMath.Atan2(double.PositiveInfinity, double.PositiveInfinity).ShouldBe(Math.PI / 4);
        DeterministicMath.Atan2(double.NegativeInfinity, double.NegativeInfinity).ShouldBe(-3 * Math.PI / 4);
        double.IsNaN(DeterministicMath.Atan2(double.NaN, 1)).ShouldBeTrue();

        DeterministicMath.Acos(1).ShouldBe(0.0);
        DeterministicMath.Acos(-1).ShouldBe(Math.PI);
        double.IsNaN(DeterministicMath.Acos(1.0000001)).ShouldBeTrue();
        double.IsNaN(DeterministicMath.Acos(double.NaN)).ShouldBeTrue();
    }

    // ---- Monotonicity ------------------------------------------------------------------------

    [Fact]
    public void Exp_Log_Atan_Pow_AreMonotonic()
    {
        double previousExp = 0, previousLog = double.NegativeInfinity, previousAtan = -2, previousPow = -1;
        for (int i = -40000; i <= 40000; i++)
        {
            double x = i / 1000.0;
            double e = DeterministicMath.Exp(x);
            e.ShouldBeGreaterThan(previousExp, $"exp {x}");
            previousExp = e;

            double a = DeterministicMath.Atan(x);
            a.ShouldBeGreaterThan(previousAtan, $"atan {x}");
            previousAtan = a;

            if (i > -40000)
            {
                double p = (i + 40000) / 80000.0;
                double l = DeterministicMath.Log(p);
                l.ShouldBeGreaterThan(previousLog, $"log {p}");
                previousLog = l;

                double pw = DeterministicMath.Pow(p, 2.4);
                pw.ShouldBeGreaterThan(previousPow, $"pow {p} 2.4");
                previousPow = pw;
            }
        }
    }

    [Fact]
    public void Sin_Cos_AreMonotonicOverAQuarterTurn()
    {
        double previousSin = -2, previousCos = 2;
        for (int i = 0; i <= 100000; i++)
        {
            double x = i * (Math.PI / 2) / 100000;
            double s = DeterministicMath.Sin(x);
            s.ShouldBeGreaterThanOrEqualTo(previousSin, $"sin {x}");
            previousSin = s;
            double c = DeterministicMath.Cos(x);
            c.ShouldBeLessThanOrEqualTo(previousCos, $"cos {x}");
            previousCos = c;
        }
    }

    [Fact]
    public void Sin_Cos_StayWithinUnitBounds_AndIdentityHolds()
    {
        for (int i = -5000; i <= 5000; i++)
        {
            double x = i * 0.0123;
            double s = DeterministicMath.Sin(x), c = DeterministicMath.Cos(x);
            Math.Abs(s).ShouldBeLessThanOrEqualTo(1.0);
            Math.Abs(c).ShouldBeLessThanOrEqualTo(1.0);
            ((s * s) + (c * c)).ShouldBe(1.0, 1e-14, $"sin²+cos² at {x}");
        }
    }

    // ---- Pinned bits: must agree on every CI platform --------------------------------------

    [Theory]
    [MemberData(nameof(PinnedBits))]
    public void PinnedInputs_ProduceExactlyTheseBits(string function, double input, double input2, long expectedBits)
    {
        double actual = function switch
        {
            "sin" => DeterministicMath.Sin(input),
            "cos" => DeterministicMath.Cos(input),
            "tan" => DeterministicMath.Tan(input),
            "exp" => DeterministicMath.Exp(input),
            "log" => DeterministicMath.Log(input),
            "pow" => DeterministicMath.Pow(input, input2),
            "atan" => DeterministicMath.Atan(input),
            "atan2" => DeterministicMath.Atan2(input, input2),
            "acos" => DeterministicMath.Acos(input),
            _ => throw new ArgumentOutOfRangeException(nameof(function)),
        };

        BitConverter.DoubleToInt64Bits(actual).ShouldBe(expectedBits,
            $"{function}({input}{(function is "pow" or "atan2" ? $", {input2}" : "")}) = {actual:R} (0x{BitConverter.DoubleToInt64Bits(actual):X16})");
    }

    /// <summary>
    /// Bit patterns recorded once from the implementation (Windows x64, .NET 10) — not from
    /// libm. A platform producing different bits here has an inexact operation somewhere
    /// (extended precision, FMA contraction, a non-IEEE division), which is the thing this test
    /// exists to catch. Update deliberately, with the reason, never to make a platform pass.
    /// </summary>
    public static TheoryData<string, double, double, long> PinnedBits() => new()
    {
        { "sin", 0.5, 0, PinnedSin0_5 },
        { "sin", 12 * Math.PI / 180, 0, PinnedSin12Deg },
        { "cos", 0.5, 0, PinnedCos0_5 },
        { "cos", 2 * Math.PI, 0, PinnedCos2Pi },
        { "tan", -12 * Math.PI / 180, 0, PinnedTanMinus12Deg },
        { "tan", 20 * Math.PI / 180, 0, PinnedTan20Deg },
        { "exp", 1, 0, PinnedExp1 },
        { "exp", -5.3, 0, PinnedExpMinus5_3 },
        { "log", 0.5, 0, PinnedLog0_5 },
        { "log", 1234.5678, 0, PinnedLog1234 },
        { "pow", 0.5, 2.4, PinnedPowHalf2_4 },
        { "pow", 0.37, 1 / 2.4, PinnedPow037Inv2_4 },
        { "atan", 0.3, 0, PinnedAtan0_3 },
        { "atan2", 1, 3, PinnedAtan2_1_3 },
        { "atan2", -2, -0.5, PinnedAtan2Minus2MinusHalf },
        { "acos", 0.3, 0, PinnedAcos0_3 },
    };

    // Recorded values (see PinnedBits).
    private const long PinnedSin0_5 = 0x3FDEAEE8744B05F0;
    private const long PinnedSin12Deg = 0x3FCA9CD9AC4258F5;
    private const long PinnedCos0_5 = 0x3FEC1528065B7D50;
    private const long PinnedCos2Pi = 0x3FF0000000000000;
    private const long PinnedTanMinus12Deg = unchecked((long)0xBFCB350DAC76234B);
    private const long PinnedTan20Deg = 0x3FD74B49CF3902D4;
    private const long PinnedExp1 = 0x4005BF0A8B14576A;
    private const long PinnedExpMinus5_3 = 0x3F747210C95C89B1;
    private const long PinnedLog0_5 = unchecked((long)0xBFE62E42FEFA39EF);
    private const long PinnedLog1234 = 0x401C7951D51791D7;
    private const long PinnedPowHalf2_4 = 0x3FC8406003B2AE5C;
    private const long PinnedPow037Inv2_4 = 0x3FE52572C5488790;
    private const long PinnedAtan0_3 = 0x3FD2A73A661EAF06;
    private const long PinnedAtan2_1_3 = 0x3FD4978FA3269EE1;
    private const long PinnedAtan2Minus2MinusHalf = unchecked((long)0xBFFD0D6A1369BD34);
    private const long PinnedAcos0_3 = 0x3FF441F5ECBEEF58;

    // ---- Helpers -----------------------------------------------------------------------------

    private static void Close(double actual, double expected, string what, double absolute = 0, double relative = Tolerance)
    {
        if (double.IsNaN(expected))
        {
            double.IsNaN(actual).ShouldBeTrue(what);
            return;
        }

        if (double.IsInfinity(expected))
        {
            actual.ShouldBe(expected, what);
            return;
        }

        double allowed = Math.Max(Math.Abs(expected) * relative, absolute);
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(allowed, $"{what}: {actual:R} vs {expected:R}");
    }

    private static void WithinOneFloatUlp(float actual, float expected, string what)
    {
        int a = BitConverter.SingleToInt32Bits(actual), e = BitConverter.SingleToInt32Bits(expected);
        Math.Abs((long)a - e).ShouldBeLessThanOrEqualTo(1L, $"{what}: {actual:R} vs {expected:R}");
    }
}
