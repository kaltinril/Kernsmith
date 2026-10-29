namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>
/// The PaintComposite operators, from W3C Compositing and Blending Level 1 (which the COLR
/// spec adopts): the Porter-Duff operators, and the separable and non-separable blend modes
/// composited with source-over. Inputs and output are premultiplied, linear-light colors.
/// </summary>
internal static class CompositeOps
{
    /// <summary>Composites <paramref name="s"/> (source) onto <paramref name="b"/> (backdrop).</summary>
    public static LinearColor Apply(CompositeMode mode, LinearColor s, LinearColor b)
    {
        float sa = s.A, ba = b.A;
        switch (mode)
        {
            case CompositeMode.Clear: return default;
            case CompositeMode.Source: return s;
            case CompositeMode.Destination: return b;
            case CompositeMode.SourceOver: return PorterDuff(s, b, 1, 1 - sa);
            case CompositeMode.DestinationOver: return PorterDuff(s, b, 1 - ba, 1);
            case CompositeMode.SourceIn: return PorterDuff(s, b, ba, 0);
            case CompositeMode.DestinationIn: return PorterDuff(s, b, 0, sa);
            case CompositeMode.SourceOut: return PorterDuff(s, b, 1 - ba, 0);
            case CompositeMode.DestinationOut: return PorterDuff(s, b, 0, 1 - sa);
            case CompositeMode.SourceAtop: return PorterDuff(s, b, ba, 1 - sa);
            case CompositeMode.DestinationAtop: return PorterDuff(s, b, 1 - ba, sa);
            case CompositeMode.Xor: return PorterDuff(s, b, 1 - ba, 1 - sa);
            case CompositeMode.Plus:
                return new LinearColor(
                    MathF.Min(1, s.R + b.R), MathF.Min(1, s.G + b.G), MathF.Min(1, s.B + b.B), MathF.Min(1, sa + ba));
        }

        // Blend modes: B(Cb, Cs) on straight colors, then
        //   co = cs·(1 − αb) + cb·(1 − αs) + αs·αb·B(Cb, Cs),  αo = αs + αb·(1 − αs).
        // Where either side is transparent the blend term vanishes, so this is plain source-over.
        if (sa <= 0 || ba <= 0)
            return PorterDuff(s, b, 1, 1 - sa);

        float sr = s.R / sa, sg = s.G / sa, sb = s.B / sa;
        float br = b.R / ba, bg = b.G / ba, bb = b.B / ba;
        float mr, mg, mb;
        if (mode <= CompositeMode.Multiply)
        {
            mr = Separable(mode, br, sr);
            mg = Separable(mode, bg, sg);
            mb = Separable(mode, bb, sb);
        }
        else
        {
            (mr, mg, mb) = NonSeparable(mode, (br, bg, bb), (sr, sg, sb));
        }

        float both = sa * ba;
        return new LinearColor(
            (s.R * (1 - ba)) + (b.R * (1 - sa)) + (both * mr),
            (s.G * (1 - ba)) + (b.G * (1 - sa)) + (both * mg),
            (s.B * (1 - ba)) + (b.B * (1 - sa)) + (both * mb),
            sa + (ba * (1 - sa)));
    }

    private static LinearColor PorterDuff(LinearColor s, LinearColor b, float fa, float fb) => new(
        (s.R * fa) + (b.R * fb),
        (s.G * fa) + (b.G * fb),
        (s.B * fa) + (b.B * fb),
        (s.A * fa) + (b.A * fb));

    private static float Separable(CompositeMode mode, float cb, float cs) => mode switch
    {
        CompositeMode.Screen => Screen(cb, cs),
        CompositeMode.Overlay => HardLight(cs, cb),
        CompositeMode.Darken => MathF.Min(cb, cs),
        CompositeMode.Lighten => MathF.Max(cb, cs),
        CompositeMode.ColorDodge => cb <= 0 ? 0 : cs >= 1 ? 1 : MathF.Min(1, cb / (1 - cs)),
        CompositeMode.ColorBurn => cb >= 1 ? 1 : cs <= 0 ? 0 : 1 - MathF.Min(1, (1 - cb) / cs),
        CompositeMode.HardLight => HardLight(cb, cs),
        CompositeMode.SoftLight => SoftLight(cb, cs),
        CompositeMode.Difference => MathF.Abs(cb - cs),
        CompositeMode.Exclusion => cb + cs - (2 * cb * cs),
        _ => cb * cs, // Multiply
    };

    private static float Screen(float cb, float cs) => cb + cs - (cb * cs);

    private static float HardLight(float cb, float cs) =>
        cs <= 0.5f ? cb * 2 * cs : Screen(cb, (2 * cs) - 1);

    private static float SoftLight(float cb, float cs)
    {
        if (cs <= 0.5f)
            return cb - ((1 - (2 * cs)) * cb * (1 - cb));

        float d = cb <= 0.25f ? ((((16 * cb) - 12) * cb) + 4) * cb : MathF.Sqrt(cb);
        return cb + (((2 * cs) - 1) * (d - cb));
    }

    private static (float R, float G, float B) NonSeparable(CompositeMode mode, (float R, float G, float B) cb, (float R, float G, float B) cs) => mode switch
    {
        CompositeMode.Hue => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),
        CompositeMode.Saturation => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),
        CompositeMode.Color => SetLum(cs, Lum(cb)),
        _ => SetLum(cb, Lum(cs)), // Luminosity
    };

    private static float Lum((float R, float G, float B) c) => (0.3f * c.R) + (0.59f * c.G) + (0.11f * c.B);

    private static float Sat((float R, float G, float B) c) =>
        MathF.Max(c.R, MathF.Max(c.G, c.B)) - MathF.Min(c.R, MathF.Min(c.G, c.B));

    private static (float R, float G, float B) SetLum((float R, float G, float B) c, float l)
    {
        float d = l - Lum(c);
        return ClipColor((c.R + d, c.G + d, c.B + d));
    }

    private static (float R, float G, float B) ClipColor((float R, float G, float B) c)
    {
        float l = Lum(c);
        float n = MathF.Min(c.R, MathF.Min(c.G, c.B));
        float x = MathF.Max(c.R, MathF.Max(c.G, c.B));
        if (n < 0)
            c = (l + ((c.R - l) * l / (l - n)), l + ((c.G - l) * l / (l - n)), l + ((c.B - l) * l / (l - n)));
        if (x > 1)
            c = (l + ((c.R - l) * (1 - l) / (x - l)), l + ((c.G - l) * (1 - l) / (x - l)), l + ((c.B - l) * (1 - l) / (x - l)));
        return c;
    }

    /// <summary>Rescales the color so max − min = <paramref name="s"/>, keeping the channel order; min goes to 0.</summary>
    private static (float R, float G, float B) SetSat((float R, float G, float B) c, float s)
    {
        Span<float> v = [c.R, c.G, c.B];
        int max = v[0] >= v[1] ? (v[0] >= v[2] ? 0 : 2) : (v[1] >= v[2] ? 1 : 2);
        int min = v[0] < v[1] ? (v[0] < v[2] ? 0 : 2) : (v[1] < v[2] ? 1 : 2);
        if (min == max)
            min = (max + 1) % 3;
        int mid = 3 - max - min;

        Span<float> r = stackalloc float[3];
        if (v[max] > v[min])
        {
            r[mid] = (v[mid] - v[min]) * s / (v[max] - v[min]);
            r[max] = s;
        }

        r[min] = 0;
        return (r[0], r[1], r[2]);
    }
}
