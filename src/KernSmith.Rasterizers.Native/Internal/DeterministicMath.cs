namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// Transcendental functions built from the operations IEEE 754 makes exact everywhere —
/// <c>+ − × ÷</c>, <c>Sqrt</c>, <c>Round</c>/<c>Floor</c>, comparisons and bit casts — so the
/// result bits are the same on every runtime, OS and CPU. <c>System.Math</c>'s versions call the
/// host C runtime, and the libms of Windows, glibc, Apple and Emscripten (browser-wasm) round
/// the last bit differently; an atlas built through them could differ between hosts.
/// </summary>
/// <remarks>
/// Range reduction and a short Taylor/Horner series in <c>double</c>, Cody–Waite style: the
/// reduction constants are split into a high part with trailing zero bits (so its product with
/// the quotient is exact) and a low remainder. Accuracy is within a few double ulps of the true
/// value for the domains Native uses (angles within about ±2^20, |y·ln x| below about 700),
/// which is far below one float ulp once narrowed; outside those domains results stay
/// deterministic but lose accuracy gracefully. Not correctly rounded, and not meant to match
/// <c>System.Math</c> bit for bit — only to match itself everywhere.
/// INVARIANT: this file uses no libm-backed <c>Math</c> member and no fused multiply-add;
/// <c>NativeSourceDeterminismTests</c> scans for both.
/// </remarks>
internal static class DeterministicMath
{
    private const double Pi = Math.PI;
    private const double HalfPi = Math.PI / 2;
    private const double QuarterPi = Math.PI / 4;
    private const double TwoOverPi = 2 / Math.PI;
    private const double Log2E = 1.4426950408889634074;

    // pi/2 in three parts (0x3FF921FB54400000, 0x3DD0B4611A400000, 0x3BE13198A2E03707): the
    // first two have 22 trailing zero bits, so k times each is exact for |k| < 2^22, and the
    // third carries the rest of the digits (residual below 2e-36).
    private const double HalfPiHigh = 1.57079632673412561417e+00;
    private const double HalfPiMid = 6.07710050359346054538e-11;
    private const double HalfPiLow = 2.91273205609335603533e-20;

    // ln 2 in two parts (0x3FE62E42FEE00000, 0x3DEA39EF35793C76); the high part has 21
    // trailing zero bits, so e times it is exact for every exponent.
    private const double Ln2High = 6.93147180369123816490e-01;
    private const double Ln2Low = 1.90821492927058770002e-10;

    private const double Sqrt2 = 1.4142135623730951;

    // ---- Exponential and logarithm -----------------------------------------------------------

    /// <summary>e raised to <paramref name="x"/>.</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;
        if (x > 709.782712893384)
            return double.PositiveInfinity;
        if (x < -745.1332191019412)
            return 0;

        // x = k ln2 + r, |r| <= ln2 / 2.
        double k = Math.Round(x * Log2E);
        double r = (x - (k * Ln2High)) - (k * Ln2Low);

        // Taylor series to r^13: the next term, r^14/14!, is below 4e-18 relative.
        double p = 1.0 / 6227020800.0;
        p = (p * r) + (1.0 / 479001600.0);
        p = (p * r) + (1.0 / 39916800.0);
        p = (p * r) + (1.0 / 3628800.0);
        p = (p * r) + (1.0 / 362880.0);
        p = (p * r) + (1.0 / 40320.0);
        p = (p * r) + (1.0 / 5040.0);
        p = (p * r) + (1.0 / 720.0);
        p = (p * r) + (1.0 / 120.0);
        p = (p * r) + (1.0 / 24.0);
        p = (p * r) + (1.0 / 6.0);
        p = (p * r) + 0.5;
        p = (p * r) + 1.0;
        p = (p * r) + 1.0;

        return ScaleByPowerOfTwo(p, (int)k);
    }

    /// <summary>The natural logarithm of <paramref name="x"/>.</summary>
    public static double Log(double x)
    {
        if (double.IsNaN(x) || x < 0)
            return double.NaN;
        if (x == 0)
            return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x))
            return double.PositiveInfinity;

        // x = m 2^e with m in [sqrt(1/2), sqrt 2).
        long bits = BitConverter.DoubleToInt64Bits(x);
        int exponent = 0;
        if (bits < 0x0010000000000000L)
        {
            // Subnormal: scale into the normal range first (exact).
            x *= 18014398509481984.0; // 2^54
            bits = BitConverter.DoubleToInt64Bits(x);
            exponent = -54;
        }

        exponent += (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L);
        if (m > Sqrt2)
        {
            m *= 0.5;
            exponent++;
        }

        // ln m = 2 atanh(s), s = (m − 1)/(m + 1): series in z = s², |s| <= 0.1716 so z <= 0.0295
        // and z^12/25 is below 1e-19.
        double f = m - 1.0;
        double s = f / (2.0 + f);
        double z = s * s;
        double series = 1.0 / 25.0;
        series = (series * z) + (1.0 / 23.0);
        series = (series * z) + (1.0 / 21.0);
        series = (series * z) + (1.0 / 19.0);
        series = (series * z) + (1.0 / 17.0);
        series = (series * z) + (1.0 / 15.0);
        series = (series * z) + (1.0 / 13.0);
        series = (series * z) + (1.0 / 11.0);
        series = (series * z) + (1.0 / 9.0);
        series = (series * z) + (1.0 / 7.0);
        series = (series * z) + (1.0 / 5.0);
        series = (series * z) + (1.0 / 3.0);
        series = (series * z) + 1.0;
        double logM = 2.0 * s * series;

        return (exponent * Ln2High) + (logM + (exponent * Ln2Low));
    }

    /// <summary><paramref name="x"/> raised to <paramref name="y"/>, with <c>System.Math.Pow</c>'s special cases.</summary>
    /// <remarks>Integer exponents up to ±1024 multiply out by repeated squaring; the rest is
    /// exp(y·ln x), whose relative error grows with |y·ln x| (about |y·ln x| · 2^-52) —
    /// negligible for the gamma-like exponents Native uses, and still far below float precision
    /// up to the overflow threshold.</remarks>
    public static double Pow(double x, double y)
    {
        if (y == 0 || x == 1)
            return 1;
        if (double.IsNaN(x) || double.IsNaN(y))
            return double.NaN;

        if (double.IsInfinity(y))
        {
            double magnitude = Math.Abs(x);
            if (magnitude == 1)
                return 1;
            return (magnitude < 1) == (y < 0) ? double.PositiveInfinity : 0;
        }

        bool integerExponent = y == Math.Floor(y);
        bool oddExponent = integerExponent && Math.Abs(y) < 9007199254740992.0 && ((long)y & 1) != 0;

        if (x == 0)
        {
            bool negativeZero = double.IsNegative(x);
            if (y < 0)
                return negativeZero && oddExponent ? double.NegativeInfinity : double.PositiveInfinity;
            return negativeZero && oddExponent ? -0.0 : 0.0;
        }

        if (double.IsInfinity(x))
        {
            if (x > 0)
                return y > 0 ? double.PositiveInfinity : 0;
            if (y > 0)
                return oddExponent ? double.NegativeInfinity : double.PositiveInfinity;
            return oddExponent ? -0.0 : 0.0;
        }

        double sign = 1;
        if (x < 0)
        {
            if (!integerExponent)
                return double.NaN;
            x = -x;
            sign = oddExponent ? -1 : 1;
        }

        if (y == 1)
            return sign * x;

        // Small integer powers by repeated squaring: exact where the result is representable
        // (2^10, (-2)^3, 0.5^-1000) and within a handful of ulps otherwise, where exp(y ln x)
        // would carry the logarithm's rounding scaled by |y ln x|.
        if (integerExponent && Math.Abs(y) <= 1024)
        {
            int n = (int)Math.Abs(y);
            double result = 1, factor = x;
            while (n > 0)
            {
                if ((n & 1) != 0)
                    result *= factor;
                factor *= factor;
                n >>= 1;
            }

            return sign * (y < 0 ? 1 / result : result);
        }

        return sign * Exp(y * Log(x));
    }

    // ---- Trigonometry ------------------------------------------------------------------------

    /// <summary>The sine of <paramref name="x"/> radians.</summary>
    public static double Sin(double x)
    {
        if (!double.IsFinite(x))
            return double.NaN;
        if (x == 0)
            return x; // keeps the sign of zero, which the reduction would lose

        var (r, quadrant) = ReduceToQuarterTurn(x);
        return quadrant switch
        {
            0 => SinReduced(r),
            1 => CosReduced(r),
            2 => -SinReduced(r),
            _ => -CosReduced(r),
        };
    }

    /// <summary>The cosine of <paramref name="x"/> radians.</summary>
    public static double Cos(double x)
    {
        if (!double.IsFinite(x))
            return double.NaN;

        var (r, quadrant) = ReduceToQuarterTurn(x);
        return quadrant switch
        {
            0 => CosReduced(r),
            1 => -SinReduced(r),
            2 => -CosReduced(r),
            _ => SinReduced(r),
        };
    }

    /// <summary>The tangent of <paramref name="x"/> radians.</summary>
    public static double Tan(double x)
    {
        if (!double.IsFinite(x))
            return double.NaN;
        if (x == 0)
            return x;

        var (r, quadrant) = ReduceToQuarterTurn(x);
        double s = SinReduced(r), c = CosReduced(r);
        return (quadrant & 1) == 0 ? s / c : -c / s;
    }

    /// <summary>The arc tangent of <paramref name="x"/>, in [−π/2, π/2].</summary>
    public static double Atan(double x)
    {
        if (double.IsNaN(x))
            return double.NaN;
        if (double.IsInfinity(x))
            return Math.CopySign(HalfPi, x);

        double a = Math.Abs(x);
        bool inverted = a > 1;
        if (inverted)
            a = 1 / a;

        // Three argument halvings, atan a = 2 atan(a / (1 + sqrt(1 + a²))), bring a within
        // tan(π/32) ≈ 0.0985; then the series in z = a², where z^9/19 is below 1e-19.
        a = a / (1 + Math.Sqrt(1 + (a * a)));
        a = a / (1 + Math.Sqrt(1 + (a * a)));
        a = a / (1 + Math.Sqrt(1 + (a * a)));
        double z = a * a;
        double series = -1.0 / 19.0;
        series = (series * z) + (1.0 / 17.0);
        series = (series * z) + (-1.0 / 15.0);
        series = (series * z) + (1.0 / 13.0);
        series = (series * z) + (-1.0 / 11.0);
        series = (series * z) + (1.0 / 9.0);
        series = (series * z) + (-1.0 / 7.0);
        series = (series * z) + (1.0 / 5.0);
        series = (series * z) + (-1.0 / 3.0);
        series = (series * z) + 1.0;
        double result = 8 * a * series;
        if (inverted)
            result = HalfPi - result;

        return Math.CopySign(result, x);
    }

    /// <summary>The angle of the point (<paramref name="x"/>, <paramref name="y"/>) from the +x axis, in (−π, π].</summary>
    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
            return double.NaN;

        if (y == 0)
            return x > 0 || (x == 0 && !double.IsNegative(x)) ? Math.CopySign(0, y) : Math.CopySign(Pi, y);
        if (x == 0)
            return Math.CopySign(HalfPi, y);

        if (double.IsInfinity(x))
        {
            if (double.IsInfinity(y))
                return Math.CopySign(x > 0 ? QuarterPi : 3 * QuarterPi, y);
            return Math.CopySign(x > 0 ? 0 : Pi, y);
        }

        if (double.IsInfinity(y))
            return Math.CopySign(HalfPi, y);

        double angle = Atan(Math.Abs(y / x));
        if (x < 0)
            angle = Pi - angle;

        return Math.CopySign(angle, y);
    }

    /// <summary>The arc cosine of <paramref name="x"/>, in [0, π]; NaN outside [−1, 1].</summary>
    public static double Acos(double x)
    {
        if (double.IsNaN(x) || x > 1 || x < -1)
            return double.NaN;
        if (x == 1)
            return 0;
        if (x == -1)
            return Pi;

        return 2 * Atan(Math.Sqrt((1 - x) / (1 + x)));
    }

    // ---- Kernels -----------------------------------------------------------------------------

    /// <summary>x = k·π/2 + r with |r| ≤ π/4; returns r and k mod 4.</summary>
    private static (double Remainder, int Quadrant) ReduceToQuarterTurn(double x)
    {
        double k = Math.Round(x * TwoOverPi);
        double r = ((x - (k * HalfPiHigh)) - (k * HalfPiMid)) - (k * HalfPiLow);
        // k is an integer-valued double; its low two bits survive the cast exactly within ±2^62.
        int quadrant = Math.Abs(k) < 4611686018427387904.0 ? (int)((long)k & 3) : 0;
        return (r, quadrant);
    }

    /// <summary>sin r for |r| ≤ π/4: Taylor series to r^15 (next term below 5e-17).</summary>
    private static double SinReduced(double r)
    {
        double z = r * r;
        double p = -1.0 / 1307674368000.0;
        p = (p * z) + (1.0 / 6227020800.0);
        p = (p * z) + (-1.0 / 39916800.0);
        p = (p * z) + (1.0 / 362880.0);
        p = (p * z) + (-1.0 / 5040.0);
        p = (p * z) + (1.0 / 120.0);
        p = (p * z) + (-1.0 / 6.0);
        return r + (r * z * p);
    }

    /// <summary>cos r for |r| ≤ π/4: Taylor series to r^16 (next term below 3e-18).</summary>
    private static double CosReduced(double r)
    {
        double z = r * r;
        double p = 1.0 / 20922789888000.0;
        p = (p * z) + (-1.0 / 87178291200.0);
        p = (p * z) + (1.0 / 479001600.0);
        p = (p * z) + (-1.0 / 3628800.0);
        p = (p * z) + (1.0 / 40320.0);
        p = (p * z) + (-1.0 / 720.0);
        p = (p * z) + (1.0 / 24.0);
        p = (p * z) + (-1.0 / 2.0);
        return 1.0 + (z * p);
    }

    /// <summary><paramref name="value"/> × 2^<paramref name="power"/> by exponent arithmetic, in
    /// steps that stay within the normal range so each step is exact (the last may round once
    /// into the subnormals, as any ldexp does).</summary>
    private static double ScaleByPowerOfTwo(double value, int power)
    {
        while (power > 1023)
        {
            value *= 8.98846567431158e+307; // 2^1023
            power -= 1023;
            if (double.IsInfinity(value))
                return value;
        }

        while (power < -1022)
        {
            value *= 2.2250738585072014e-308; // 2^-1022
            power += 1022;
            if (value == 0)
                return value;
        }

        return value * BitConverter.Int64BitsToDouble((long)(power + 1023) << 52);
    }
}
