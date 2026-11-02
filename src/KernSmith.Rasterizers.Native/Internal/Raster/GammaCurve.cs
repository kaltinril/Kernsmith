namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// <see cref="RasterOptions.Gamma"/> applied to 8-bit coverage, with the FreeType backend's
/// contract so a config renders the same tone on either backend: 1.8 (the option's default,
/// Hiero's <c>font.gamma</c>) is the identity, and any other value maps coverage through
/// <c>(c/255)^(1.8/gamma)</c> — above 1.8 heavier, below it lighter. 0 and 255 are fixed, so
/// solid ink and empty pixels never move; mono output, which is only those, is unaffected.
/// </summary>
internal static class GammaCurve
{
    /// <summary>The gamma that maps coverage to itself.</summary>
    public const float Identity = 1.8f;

    [ThreadStatic]
    private static byte[]? _table;

    [ThreadStatic]
    private static float _tableGamma;

    /// <summary>True when <paramref name="gamma"/> changes coverage: positive and not the identity.</summary>
    public static bool Changes(float gamma) => gamma > 0f && Math.Abs((Identity / gamma) - 1f) >= 1e-6f;

    /// <summary>Maps every byte of <paramref name="coverage"/> through the curve, in place; a no-op at the identity or an invalid gamma.</summary>
    public static void Apply(Span<byte> coverage, float gamma)
    {
        if (!Changes(gamma))
            return;

        var table = Table(gamma);
        foreach (ref byte value in coverage)
            value = table[value];
    }

    /// <summary>The 256-entry lookup for a gamma, rebuilt only when the gamma changes on this thread.</summary>
    private static byte[] Table(float gamma)
    {
        if (_table is { } cached && _tableGamma.Equals(gamma))
            return cached;

        var table = new byte[256];
        double exponent = Identity / gamma;
        table[255] = 255;
        for (int i = 1; i < 255; i++)
            table[i] = (byte)Math.Clamp((int)Math.Round(Math.Pow(i / 255.0, exponent) * 255.0, MidpointRounding.AwayFromZero), 0, 255);

        _table = table;
        _tableGamma = gamma;
        return table;
    }
}
