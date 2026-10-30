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

    /// <summary>Decodes an 8-bit sRGB value to linear light in 0..1.</summary>
    public static float ToLinear(byte value) => ByteToLinear[value];

    /// <summary>
    /// For each code k from 1 to 255, the smallest float <see cref="EncodeDirect"/> takes to k or
    /// above, so a value's code is the number of thresholds at or below it.
    /// </summary>
    private static readonly float[] EncodeThresholds = BuildEncodeThresholds();

    /// <summary>Encodes a linear-light channel in 0..1 (clamped) as an 8-bit sRGB value.</summary>
    /// <remarks>
    /// INVARIANT: equal to <see cref="EncodeDirect"/> for every float, which is a non-decreasing
    /// step function of its input (checked over every float in [0, 1]): below zero, NaN included,
    /// it is 0 and above one 255, as the clamp makes it. The thresholds come from
    /// <see cref="EncodeDirect"/> itself, so the two cannot drift apart; this only replaces a
    /// <c>Math.Pow</c> per channel with eight comparisons.
    /// </remarks>
    public static byte Encode(float linear)
    {
        if (!(linear > 0f))
            return 0;

        var thresholds = EncodeThresholds;
        int low = 0, high = thresholds.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (thresholds[middle] <= linear)
                low = middle + 1;
            else
                high = middle;
        }

        return (byte)low;
    }

    /// <summary><see cref="Encode"/> by the formula, for building and checking the thresholds.</summary>
    internal static byte EncodeDirect(float linear)
    {
        double c = Math.Clamp((double)linear, 0, 1);
        double encoded = c <= 0.0031308 ? c * 12.92 : (1.055 * Math.Pow(c, 1 / 2.4)) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(encoded * 255), 0, 255);
    }

    private static float[] BuildEncodeThresholds()
    {
        // Floats in [0, 1] order like their bit patterns, so each threshold is a binary search
        // over the patterns from +0 to 1.0.
        var thresholds = new float[255];
        int one = BitConverter.SingleToInt32Bits(1f);
        for (int code = 1; code <= 255; code++)
        {
            int low = 0, high = one;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (EncodeDirect(BitConverter.Int32BitsToSingle(middle)) >= code)
                    high = middle;
                else
                    low = middle + 1;
            }

            thresholds[code - 1] = BitConverter.Int32BitsToSingle(low);
        }

        return thresholds;
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
