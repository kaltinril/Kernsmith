namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>A premultiplied, linear-light color in doubles (test oracle).</summary>
internal readonly record struct LinearColor(double R, double G, double B, double A);

/// <summary>
/// Test oracle for the sRGB transfer function (IEC 61966-2-1, as restated in CSS Color 4 §10.2)
/// and the premultiplied linear-light interpolation the CPAL chapter requires for COLR gradients.
/// </summary>
internal static class Srgb
{
    public static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    public static double FromLinear(double c) => c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;

    /// <summary>A 0xRRGGBBAA palette color with an extra alpha factor, premultiplied in linear light.</summary>
    public static LinearColor Premultiplied(uint rgba, double alpha)
    {
        double a = ((rgba & 0xFF) / 255.0) * alpha;
        return new LinearColor(
            ToLinear((rgba >> 24) / 255.0) * a,
            ToLinear(((rgba >> 16) & 0xFF) / 255.0) * a,
            ToLinear(((rgba >> 8) & 0xFF) / 255.0) * a,
            a);
    }

    public static LinearColor Lerp(LinearColor a, LinearColor b, double t) => new(
        a.R + ((b.R - a.R) * t), a.G + ((b.G - a.G) * t), a.B + ((b.B - a.B) * t), a.A + ((b.A - a.A) * t));

    /// <summary>Un-premultiplies and encodes to straight 8-bit sRGB.</summary>
    public static (byte R, byte G, byte B, byte A) ToStraightBytes(LinearColor c)
    {
        double a = Math.Clamp(c.A, 0, 1);
        byte a8 = (byte)Math.Round(a * 255);
        if (a8 == 0)
            return (0, 0, 0, 0);

        return (Encode(c.R / a), Encode(c.G / a), Encode(c.B / a), a8);
    }

    private static byte Encode(double linear) => (byte)Math.Round(FromLinear(Math.Clamp(linear, 0, 1)) * 255);
}

/// <summary>
/// Test oracle for W3C Compositing and Blending Level 1: the Porter-Duff operators and the
/// separable and non-separable blend modes, in the COLR CompositeMode numbering.
/// </summary>
internal static class W3C
{
    public static LinearColor Composite(int mode, LinearColor s, LinearColor b)
    {
        double @as = s.A, ab = b.A;
        switch (mode)
        {
            case 0: return default;
            case 1: return PorterDuff(s, b, 1, 0);
            case 2: return PorterDuff(s, b, 0, 1);
            case 3: return PorterDuff(s, b, 1, 1 - @as);
            case 4: return PorterDuff(s, b, 1 - ab, 1);
            case 5: return PorterDuff(s, b, ab, 0);
            case 6: return PorterDuff(s, b, 0, @as);
            case 7: return PorterDuff(s, b, 1 - ab, 0);
            case 8: return PorterDuff(s, b, 0, 1 - @as);
            case 9: return PorterDuff(s, b, ab, 1 - @as);
            case 10: return PorterDuff(s, b, 1 - ab, @as);
            case 11: return PorterDuff(s, b, 1 - ab, 1 - @as);
            case 12:
                return new LinearColor(Math.Min(1, s.R + b.R), Math.Min(1, s.G + b.G), Math.Min(1, s.B + b.B), Math.Min(1, @as + ab));
            case >= 13 and <= 27:
                break;
            default:
                return default;
        }

        // Straight colors, then B(Cb, Cs), then source-over with the blend folded in.
        double[] cs = [s.R / @as, s.G / @as, s.B / @as];
        double[] cb = [b.R / ab, b.G / ab, b.B / ab];
        double[] mixed = mode <= 23 ? [Separable(mode, cb[0], cs[0]), Separable(mode, cb[1], cs[1]), Separable(mode, cb[2], cs[2])] : NonSeparable(mode, cb, cs);

        double Channel(int i, double sp, double bp) => (sp * (1 - ab)) + (bp * (1 - @as)) + (@as * ab * mixed[i]);
        return new LinearColor(Channel(0, s.R, b.R), Channel(1, s.G, b.G), Channel(2, s.B, b.B), @as + (ab * (1 - @as)));
    }

    private static LinearColor PorterDuff(LinearColor s, LinearColor b, double fa, double fb) =>
        new((s.R * fa) + (b.R * fb), (s.G * fa) + (b.G * fb), (s.B * fa) + (b.B * fb), (s.A * fa) + (b.A * fb));

    private static double Separable(int mode, double cb, double cs) => mode switch
    {
        13 => cb + cs - (cb * cs),                                          // screen
        14 => HardLight(cs: cb, cb: cs),                                    // overlay = hard-light, swapped
        15 => Math.Min(cb, cs),                                             // darken
        16 => Math.Max(cb, cs),                                             // lighten
        17 => cb == 0 ? 0 : cs >= 1 ? 1 : Math.Min(1, cb / (1 - cs)),       // color-dodge
        18 => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - Math.Min(1, (1 - cb) / cs),   // color-burn
        19 => HardLight(cb, cs),                                            // hard-light
        20 => SoftLight(cb, cs),                                            // soft-light
        21 => Math.Abs(cb - cs),                                            // difference
        22 => cb + cs - (2 * cb * cs),                                      // exclusion
        23 => cb * cs,                                                      // multiply
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static double HardLight(double cb, double cs) =>
        cs <= 0.5 ? cb * 2 * cs : cb + ((2 * cs) - 1) - (cb * ((2 * cs) - 1));

    private static double SoftLight(double cb, double cs)
    {
        if (cs <= 0.5)
            return cb - ((1 - (2 * cs)) * cb * (1 - cb));
        double d = cb <= 0.25 ? ((((16 * cb) - 12) * cb) + 4) * cb : Math.Sqrt(cb);
        return cb + (((2 * cs) - 1) * (d - cb));
    }

    private static double[] NonSeparable(int mode, double[] cb, double[] cs) => mode switch
    {
        24 => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),   // hue
        25 => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),   // saturation
        26 => SetLum(cs, Lum(cb)),                    // color
        27 => SetLum(cb, Lum(cs)),                    // luminosity
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static double Lum(double[] c) => (0.3 * c[0]) + (0.59 * c[1]) + (0.11 * c[2]);

    private static double Sat(double[] c) => c.Max() - c.Min();

    private static double[] SetLum(double[] c, double l)
    {
        double d = l - Lum(c);
        return ClipColor([c[0] + d, c[1] + d, c[2] + d]);
    }

    private static double[] ClipColor(double[] c)
    {
        double l = Lum(c), n = c.Min(), x = c.Max();
        var r = (double[])c.Clone();
        if (n < 0)
            for (int i = 0; i < 3; i++) r[i] = l + ((r[i] - l) * l / (l - n));
        if (x > 1)
            for (int i = 0; i < 3; i++) r[i] = l + ((r[i] - l) * (1 - l) / (x - l));
        return r;
    }

    private static double[] SetSat(double[] c, double s)
    {
        int[] order = [.. Enumerable.Range(0, 3).OrderBy(i => c[i])];
        int min = order[0], mid = order[1], max = order[2];
        var r = new double[3];
        if (c[max] > c[min])
        {
            r[mid] = (c[mid] - c[min]) * s / (c[max] - c[min]);
            r[max] = s;
        }

        r[min] = 0;
        return r;
    }
}
