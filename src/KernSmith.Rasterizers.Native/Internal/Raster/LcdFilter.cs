namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// A symmetric low-pass FIR kernel applied across a row of 3x horizontal (one-third-pixel)
/// coverage samples to produce LCD subpixel values.
/// </summary>
/// <remarks>
/// Unfiltered one-third-pixel coverage puts a hard colour fringe on every vertical edge. Each
/// output subpixel is instead the weighted mean of its neighbours, trading a little sharpness for
/// colour balance. Weights are integers over a common <see cref="Denominator"/>; they sum to it,
/// so the filter conserves ink, and they are symmetric, so it does not move the glyph.
/// </remarks>
internal sealed class LcdFilter
{
    private readonly int[] _weights;

    /// <summary>
    /// A one-pixel box: each subpixel is the coverage of the whole-pixel window centred on it.
    /// Identical to rendering the glyph three times at pixel resolution with the outline shifted
    /// by +1/3, 0 and -1/3 px (the "Harmony" description of LCD rendering), and what the
    /// FreeType backend's LCD output measures as. Spills one subpixel onto each side.
    /// </summary>
    public static LcdFilter PixelBox { get; } = new([1, 1, 1], 3);

    /// <summary>
    /// The ClearType-style 5-tap kernel [1, 2, 3, 2, 1] / 9 — the pixel box convolved with
    /// itself. Softer colour fringes than <see cref="PixelBox"/>, slightly blurrier stems.
    /// Spills two subpixels onto each side.
    /// </summary>
    public static LcdFilter Triangle { get; } = new([1, 2, 3, 2, 1], 9);

    /// <summary>Creates a kernel.</summary>
    /// <param name="weights">An odd number of taps, centre tap in the middle.</param>
    /// <param name="denominator">What every weight is divided by.</param>
    /// <exception cref="ArgumentException">If <paramref name="weights"/> has an even length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="denominator"/> is not positive.</exception>
    public LcdFilter(int[] weights, int denominator)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length % 2 == 0)
            throw new ArgumentException("An LCD filter needs an odd number of taps.", nameof(weights));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        _weights = (int[])weights.Clone();
        Denominator = denominator;
    }

    /// <summary>The taps, left to right.</summary>
    public IReadOnlyList<int> Weights => _weights;

    /// <summary>What the weighted sum is divided by.</summary>
    public int Denominator { get; }

    /// <summary>Taps on each side of the centre: how many subpixels the filter spills ink outward.</summary>
    public int Radius => _weights.Length / 2;

    /// <summary>
    /// The filtered value of subpixel <paramref name="index"/>, rounded to the nearest level.
    /// Samples outside <paramref name="subpixels"/> read as zero, so <paramref name="index"/>
    /// may lie up to <see cref="Radius"/> beyond either end.
    /// </summary>
    public byte Sample(ReadOnlySpan<byte> subpixels, int index)
    {
        int sum = 0;
        int first = index - Radius;
        for (int k = 0; k < _weights.Length; k++)
        {
            int i = first + k;
            if ((uint)i < (uint)subpixels.Length)
                sum += _weights[k] * subpixels[i];
        }

        return (byte)Math.Min(255, (sum + (Denominator / 2)) / Denominator);
    }
}
