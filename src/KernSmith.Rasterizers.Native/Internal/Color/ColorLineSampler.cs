namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>
/// Evaluates a COLR color line at a position t: extend mode, then linear interpolation of
/// premultiplied, linear-light stop colors.
/// </summary>
/// <remarks>
/// The repeat and reflect interval is the span of the stops, first offset to last. Stops
/// arrive sorted (stable), so where several share an offset the first is used below it and the
/// last at and above it. No stops is transparent everywhere; one stop is that color everywhere.
/// </remarks>
internal readonly struct ColorLineSampler
{
    private readonly ColorStop[] _stops;

    private ColorLineSampler(ColorStop[] stops, ColorLineExtend extend)
    {
        _stops = stops;
        Extend = extend;
    }

    /// <summary>The extend mode.</summary>
    public ColorLineExtend Extend { get; }

    /// <summary>
    /// A sampler for the line; false when it paints nothing — no stops, or every stop at one
    /// offset with repeat or reflect (ill-formed: nothing must be rendered).
    /// </summary>
    public static bool TryCreate(ColorStop[] sortedStops, ColorLineExtend extend, out ColorLineSampler sampler)
    {
        sampler = new ColorLineSampler(sortedStops, extend);
        if (sortedStops.Length == 0)
            return false;

        bool zeroLength = sortedStops[^1].Offset == sortedStops[0].Offset;
        return !(zeroLength && sortedStops.Length > 1 && extend != ColorLineExtend.Pad);
    }

    /// <summary>The color at <paramref name="t"/>.</summary>
    public LinearColor Sample(double t)
    {
        var stops = _stops;
        if (stops.Length == 1)
            return stops[0].Color;

        double first = stops[0].Offset;
        double last = stops[^1].Offset;
        double length = last - first;
        if (length > 0 && double.IsFinite(t))
        {
            if (Extend == ColorLineExtend.Repeat)
            {
                t = first + PositiveModulo(t - first, length);
            }
            else if (Extend == ColorLineExtend.Reflect)
            {
                double u = (t - first) / length;
                double period = Math.Floor(u);
                double fraction = u - period;
                if (PositiveModulo(period, 2) >= 1)
                    fraction = 1 - fraction;
                t = first + (fraction * length);
            }
        }

        if (!(t >= first))
            return stops[0].Color;
        if (t >= last)
            return stops[^1].Color;

        // The last stop at or below t, and the first one above it.
        int below = 0;
        while (stops[below + 1].Offset <= t)
            below++;
        var a = stops[below];
        var b = stops[below + 1];
        return LinearColor.Lerp(a.Color, b.Color, (float)((t - a.Offset) / (b.Offset - a.Offset)));
    }

    private static double PositiveModulo(double value, double modulus)
    {
        double r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
