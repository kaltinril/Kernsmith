namespace KernSmith.Imaging;

/// <summary>A decoded PNG: 8-bit RGBA pixels with straight (not premultiplied) alpha.</summary>
public sealed class PngImage
{
    /// <summary>Wraps decoded pixels.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">Row-major, top row first, 4 bytes per pixel (R, G, B, A); <c>width * 4</c> bytes per row.</param>
    public PngImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength != (long)width * height * 4)
            throw new ArgumentException($"Expected {(long)width * height * 4} bytes for a {width}x{height} RGBA image, got {pixels.LongLength}.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Row-major RGBA, top row first, <c>Width * 4</c> bytes per row, straight alpha. Samples are
    /// as stored in the file: no gamma or color-profile conversion is applied.
    /// </summary>
    public byte[] Pixels { get; }
}
