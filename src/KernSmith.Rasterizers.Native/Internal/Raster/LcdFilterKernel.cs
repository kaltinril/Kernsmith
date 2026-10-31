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
internal sealed class LcdFilterKernel
{
    private readonly int[] _weights;

    /// <summary>
    /// <see cref="Sample"/>'s rounding and clamp for every weighted sum the taps can produce,
    /// indexed by sum minus <see cref="_lowestSum"/>, so the row filter divides nothing.
    /// </summary>
    private readonly byte[] _levels;
    private readonly int _lowestSum;

    /// <summary>
    /// A one-pixel box: each subpixel is the coverage of the whole-pixel window centred on it.
    /// Identical to rendering the glyph three times at pixel resolution with the outline shifted
    /// by +1/3, 0 and -1/3 px (the "Harmony" description of LCD rendering), and what the
    /// FreeType backend's LCD output measures as. Spills one subpixel onto each side.
    /// </summary>
    public static LcdFilterKernel PixelBox { get; } = new([1, 1, 1], 3);

    /// <summary>
    /// The ClearType-style 5-tap kernel [1, 2, 3, 2, 1] / 9 — the pixel box convolved with
    /// itself. Softer colour fringes than <see cref="PixelBox"/>, slightly blurrier stems.
    /// Spills two subpixels onto each side.
    /// </summary>
    public static LcdFilterKernel Triangle { get; } = new([1, 2, 3, 2, 1], 9);

    /// <summary>No filtering: a single tap, so each subpixel keeps its own one-third-pixel coverage.</summary>
    public static LcdFilterKernel Identity { get; } = new([1], 1);

    /// <summary>The kernel for a public <see cref="LcdFilter"/> preset.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="filter"/> is not a defined preset.</exception>
    public static LcdFilterKernel For(LcdFilter filter) => filter switch
    {
        // Native's default is the pixel box: it is what the FreeType backend's LCD output
        // measures as, and the output LCD had before the filter was selectable.
        LcdFilter.Default or LcdFilter.Light => PixelBox,
        LcdFilter.Smooth => Triangle,
        LcdFilter.None => Identity,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unknown LCD filter."),
    };

    /// <summary>Creates a kernel.</summary>
    /// <param name="weights">An odd number of taps, centre tap in the middle.</param>
    /// <param name="denominator">What every weight is divided by.</param>
    /// <exception cref="ArgumentException">If <paramref name="weights"/> has an even length.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="denominator"/> is not positive.</exception>
    public LcdFilterKernel(int[] weights, int denominator)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Length % 2 == 0)
            throw new ArgumentException("An LCD filter needs an odd number of taps.", nameof(weights));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        _weights = (int[])weights.Clone();
        Denominator = denominator;

        int lowest = 0, highest = 0;
        foreach (int weight in _weights)
        {
            if (weight < 0)
                lowest += weight * 255;
            else
                highest += weight * 255;
        }

        _lowestSum = lowest;
        _levels = new byte[highest - lowest + 1];
        for (int sum = lowest; sum <= highest; sum++)
            _levels[sum - lowest] = Level(sum);
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

        return Level(sum);
    }

    /// <summary>
    /// <see cref="Sample"/> for the run of subpixels starting at <paramref name="first"/>, one
    /// per element of <paramref name="filtered"/>; <paramref name="first"/> may lie any distance
    /// before the row and the run any distance past it.
    /// </summary>
    public void FilterRow(ReadOnlySpan<byte> subpixels, int first, Span<byte> filtered)
    {
        int radius = Radius, taps = _weights.Length;
        int count = filtered.Length;

        // Interior subpixels, whose taps all lie inside the row, need no bounds tests.
        int interiorStart = Math.Clamp(radius - first, 0, count);
        int interiorEnd = Math.Clamp(subpixels.Length - radius - first, interiorStart, count);

        for (int j = 0; j < interiorStart; j++)
            filtered[j] = Sample(subpixels, first + j);

        var weights = _weights.AsSpan();
        var levels = _levels.AsSpan();
        for (int j = interiorStart; j < interiorEnd; j++)
        {
            var window = subpixels.Slice(first + j - radius, taps);
            int sum = 0;
            for (int k = 0; k < weights.Length; k++)
                sum += weights[k] * window[k];
            filtered[j] = levels[sum - _lowestSum];
        }

        for (int j = interiorEnd; j < count; j++)
            filtered[j] = Sample(subpixels, first + j);
    }

    /// <summary>A weighted sum divided by <see cref="Denominator"/>, rounded half up and capped at 255.</summary>
    private byte Level(int sum) => (byte)Math.Min(255, (sum + (Denominator / 2)) / Denominator);
}
