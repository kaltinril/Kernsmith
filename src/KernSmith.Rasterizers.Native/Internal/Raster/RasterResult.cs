namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// One rasterized glyph bitmap: 8-bit grayscale coverage plus where it sits relative to the
/// pen position. Phase 165 converts this into the public <c>RasterizedGlyph</c>.
/// </summary>
internal sealed class RasterResult
{
    /// <summary>Creates a result over an already-rendered bitmap.</summary>
    public RasterResult(byte[] bitmap, int width, int height, int bearingX, int bearingY)
    {
        Bitmap = bitmap;
        Width = width;
        Height = height;
        BearingX = bearingX;
        BearingY = bearingY;
    }

    /// <summary>
    /// Coverage values, one byte per pixel, row-major with no row padding (pitch == width).
    /// Freshly allocated and owned by the caller — never pooled, because consumers hold it
    /// for the lifetime of the glyph.
    /// </summary>
    public byte[] Bitmap { get; }

    /// <summary>Bitmap width in pixels.</summary>
    public int Width { get; }

    /// <summary>Bitmap height in pixels.</summary>
    public int Height { get; }

    /// <summary>Horizontal offset in pixels from the pen position to the bitmap's left edge.</summary>
    public int BearingX { get; }

    /// <summary>Vertical offset in pixels from the baseline up to the bitmap's top edge.</summary>
    public int BearingY { get; }
}
