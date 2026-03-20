namespace Bmfontier;

/// <summary>
/// A custom glyph image to replace or add as a font glyph.
/// Users are responsible for decoding their images into raw pixel data.
/// </summary>
public sealed record CustomGlyph(
    /// <summary>Width of the glyph image in pixels.</summary>
    int Width,

    /// <summary>Height of the glyph image in pixels.</summary>
    int Height,

    /// <summary>Raw pixel data in the specified format.</summary>
    byte[] PixelData,

    /// <summary>Pixel format of the data. Default is RGBA.</summary>
    PixelFormat Format = PixelFormat.Rgba32,

    /// <summary>Optional custom x-advance. When null, uses the glyph width.</summary>
    int? XAdvance = null);
