using KernSmith.Output.Model;

namespace KernSmith.Output;

/// <summary>
/// One glyph added by <see cref="BmFontIncrementalSession.AddGlyphs(string)"/>: its raw
/// pixel bytes plus the atlas placement the session reserved for it. The caller blits the
/// bytes into its own texture.
/// </summary>
/// <remarks>
/// <b>Blit contract</b>: the glyph pixels go at <c>(X + Padding.Left, Y + Padding.Up)</c>
/// on page <see cref="PageIndex"/> — the same rule the atlas builder uses for full
/// generation. <see cref="X"/>/<see cref="Y"/> are the origin of the packed cell
/// (which includes padding); <see cref="Char"/> carries the padded cell dimensions.
/// </remarks>
public sealed class AddedGlyph
{
    private byte[]? _pixels;

    /// <summary>Unicode codepoint of the glyph.</summary>
    public int Codepoint { get; }

    /// <summary>Glyph bitmap width in pixels (unpadded).</summary>
    public int Width { get; }

    /// <summary>Glyph bitmap height in pixels (unpadded).</summary>
    public int Height { get; }

    /// <summary>Zero-based atlas page the glyph was placed on.</summary>
    public int PageIndex { get; }

    /// <summary>X origin of the packed cell on the page.</summary>
    public int X { get; }

    /// <summary>Y origin of the packed cell on the page.</summary>
    public int Y { get; }

    /// <summary>Ready-to-use BMFont char entry (padded dimensions, offsets, advance).</summary>
    public CharEntry Char { get; }

    /// <summary>
    /// The rasterization pipeline's own glyph buffer — zero-copy, in <see cref="RawFormat"/>
    /// with <see cref="Pitch"/> bytes per row. The fastest path for callers that can consume
    /// the native format directly. Treat as read-only.
    /// </summary>
    public byte[] RawPixels { get; }

    /// <summary>Pixel format of <see cref="RawPixels"/> (usually <see cref="PixelFormat.Grayscale8"/>).</summary>
    public PixelFormat RawFormat { get; }

    /// <summary>Bytes per row of <see cref="RawPixels"/>.</summary>
    public int Pitch { get; }

    /// <summary>
    /// The glyph as tightly-packed RGBA32 (<see cref="Width"/> * <see cref="Height"/> * 4 bytes),
    /// ready for e.g. MonoGame's <c>Texture2D.SetData</c>. Grayscale glyphs are promoted to
    /// white with the coverage value as alpha — the canonical Angelcode BMFont layout.
    /// Computed lazily on first access and cached; callers that read only
    /// <see cref="RawPixels"/> never pay for the expansion.
    /// </summary>
    public byte[] Pixels => _pixels ??= ExpandToRgba();

    internal AddedGlyph(
        int codepoint, int width, int height, int pageIndex, int x, int y,
        CharEntry charEntry, byte[] rawPixels, PixelFormat rawFormat, int pitch)
    {
        Codepoint = codepoint;
        Width = width;
        Height = height;
        PageIndex = pageIndex;
        X = x;
        Y = y;
        Char = charEntry;
        RawPixels = rawPixels;
        RawFormat = rawFormat;
        Pitch = pitch;
    }

    private byte[] ExpandToRgba()
    {
        var rgba = new byte[Width * Height * 4];

        if (RawFormat == PixelFormat.Rgba32)
        {
            // Already RGBA — copy row by row to drop any pitch slack.
            var rowBytes = Width * 4;
            for (var row = 0; row < Height; row++)
            {
                var srcOffset = row * Pitch;
                if (srcOffset + rowBytes > RawPixels.Length)
                    break;
                Array.Copy(RawPixels, srcOffset, rgba, row * rowBytes, rowBytes);
            }
            return rgba;
        }

        // Grayscale8 → (255, 255, 255, coverage), matching AtlasPage.GetRgbaPixelData.
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                var srcIdx = row * Pitch + col;
                if (srcIdx >= RawPixels.Length)
                    continue;

                var dstIdx = (row * Width + col) * 4;
                rgba[dstIdx] = 255;
                rgba[dstIdx + 1] = 255;
                rgba[dstIdx + 2] = 255;
                rgba[dstIdx + 3] = RawPixels[srcIdx];
            }
        }
        return rgba;
    }
}
