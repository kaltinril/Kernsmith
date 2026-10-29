namespace KernSmith.Imaging;

/// <summary>The PNG color type from the <c>IHDR</c> chunk.</summary>
public enum PngColorType : byte
{
    /// <summary>Grayscale samples (bit depths 1, 2, 4, 8, 16).</summary>
    Grayscale = 0,

    /// <summary>Red, green and blue samples (bit depths 8, 16).</summary>
    Truecolor = 2,

    /// <summary>Palette indices into <c>PLTE</c> (bit depths 1, 2, 4, 8).</summary>
    Indexed = 3,

    /// <summary>Grayscale and alpha samples (bit depths 8, 16).</summary>
    GrayscaleAlpha = 4,

    /// <summary>Red, green, blue and alpha samples (bit depths 8, 16).</summary>
    TruecolorAlpha = 6
}
