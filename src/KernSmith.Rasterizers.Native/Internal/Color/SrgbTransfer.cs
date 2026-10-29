using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>
/// The sRGB transfer function (IEC 61966-2-1): CPAL colors are non-linear sRGB, while COLR
/// version 1 interpolates gradients and composites on linear-light values.
/// </summary>
internal static class SrgbTransfer
{
    private static readonly float[] ByteToLinear = BuildDecodeTable();

    /// <summary>A palette color with an extra alpha factor (clamped to 0..1), premultiplied in linear light.</summary>
    public static LinearColor ToLinearPremultiplied(PaletteColor color, float alpha)
    {
        float a = color.A / 255f * Math.Clamp(alpha, 0f, 1f);
        return new LinearColor(ByteToLinear[color.R] * a, ByteToLinear[color.G] * a, ByteToLinear[color.B] * a, a);
    }

    /// <summary>Encodes a linear-light channel in 0..1 (clamped) as an 8-bit sRGB value.</summary>
    public static byte Encode(float linear)
    {
        double c = Math.Clamp((double)linear, 0, 1);
        double encoded = c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(encoded * 255), 0, 255);
    }

    private static float[] BuildDecodeTable()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }

        return table;
    }
}
