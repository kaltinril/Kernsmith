namespace KernSmith;

/// <summary>
/// A custom glyph image that replaces or adds a glyph in the font.
/// You must decode images to raw pixels yourself before passing them here.
/// </summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="PixelData">Raw pixel data (not PNG/JPG -- already decoded).</param>
/// <param name="Format">Pixel format. Default is RGBA.</param>
/// <param name="XAdvance">Cursor advance after this glyph. When null, uses the glyph width.</param>
public sealed record CustomGlyph(
    int Width,
    int Height,
    byte[] PixelData,
    PixelFormat Format = PixelFormat.Rgba32,
    int? XAdvance = null);
