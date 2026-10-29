using KernSmith.Font.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// Maps user-scale axis values to the normalized coordinates every variation table is keyed on,
/// following the OpenType overview's "Coordinate scales and normalization": 16.16 inputs and
/// arithmetic, clamping to [-1, 1], <c>avar</c> segment maps, then rounding to 2.14.
/// </summary>
internal static class AxisNormalizer
{
    private const int One = 0x10000;

    /// <summary>
    /// Normalizes one coordinate per axis, in <paramref name="axes"/> (<c>fvar</c>) order.
    /// </summary>
    /// <param name="axes">The font's axes, in <c>fvar</c> order.</param>
    /// <param name="userValues">Requested user-scale values by axis tag. Missing axes stay at
    /// their default; tags the font does not define are ignored.</param>
    /// <param name="avar">The font's <c>avar</c> table, if it has one.</param>
    /// <returns>Normalized coordinates, each an exact F2DOT14 value.</returns>
    public static float[] Normalize(
        IReadOnlyList<VariationAxis> axes, IReadOnlyDictionary<string, float> userValues, AvarTable? avar)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(userValues);

        var coordinates = new float[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            var axis = axes[i];
            if (!userValues.TryGetValue(axis.Tag, out float user))
                continue;

            int min = ToFixed(axis.MinValue);
            int def = ToFixed(axis.DefaultValue);
            int max = ToFixed(axis.MaxValue);
            int value = Math.Clamp(ToFixed(user), Math.Min(min, def), Math.Max(max, def));

            int normalized = 0;
            if (value < def)
                normalized = -FixedDivide(def - value, def - min);
            else if (value > def)
                normalized = FixedDivide(value - def, max - def);

            normalized = Math.Clamp(normalized, -One, One);

            if (avar is not null)
                normalized = Math.Clamp(avar.Map(i, normalized), -One, One);

            // 16.16 -> 2.14: add 2, then an arithmetic (sign-extending) shift right by 2.
            int f2Dot14 = (normalized + 2) >> 2;
            coordinates[i] = f2Dot14 / 16384f;
        }

        return coordinates;
    }

    /// <summary>
    /// Converts a user value to 16.16 as the spec prescribes: a remainder of half a 1/65536 step
    /// or more rounds up, anything smaller truncates.
    /// </summary>
    internal static int ToFixed(double value) => (int)Math.Floor((value * One) + 0.5);

    /// <summary>A 16.16 quotient rounded to the nearest step; 0 for a zero divisor.</summary>
    internal static int FixedDivide(long numerator, long denominator) =>
        denominator == 0 ? 0 : (int)Math.Round((double)numerator * One / denominator, MidpointRounding.AwayFromZero);
}
