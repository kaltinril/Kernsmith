namespace KernSmith.Imaging;

/// <summary>
/// Size limits checked against the <c>IHDR</c> before anything is allocated, so a hostile file
/// cannot make the decoder reserve or inflate more than the caller allows.
/// </summary>
public sealed class PngDecodeLimits
{
    /// <summary>16384 px per side and 64 Mi pixels (256 MiB of RGBA output).</summary>
    public static PngDecodeLimits Default { get; } = new(16384, 16384, 64L * 1024 * 1024);

    /// <summary>Creates limits.</summary>
    /// <param name="maxWidth">Largest accepted width, in pixels.</param>
    /// <param name="maxHeight">Largest accepted height, in pixels.</param>
    /// <param name="maxPixels">Largest accepted width × height.</param>
    public PngDecodeLimits(int maxWidth, int maxHeight, long maxPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixels);
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MaxPixels = maxPixels;
    }

    /// <summary>Largest accepted width, in pixels.</summary>
    public int MaxWidth { get; }

    /// <summary>Largest accepted height, in pixels.</summary>
    public int MaxHeight { get; }

    /// <summary>Largest accepted width × height.</summary>
    public long MaxPixels { get; }
}
