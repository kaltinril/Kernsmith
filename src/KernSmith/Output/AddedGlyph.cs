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
    private byte[]? _premultipliedPixels;

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

    /// <summary>
    /// The glyph as tightly-packed <b>premultiplied</b> RGBA32 (<see cref="Width"/> *
    /// <see cref="Height"/> * 4 bytes) — each color channel scaled by its alpha, for
    /// premultiplied-alpha pipelines such as MonoGame/XNA's default
    /// <c>BlendState.AlphaBlend</c>; use <see cref="Pixels"/> with
    /// <c>BlendState.NonPremultiplied</c> otherwise. Grayscale glyphs become
    /// <c>(a, a, a, a)</c> — white coverage premultiplied by the coverage value.
    /// Computed lazily on first access and cached; callers that read only
    /// <see cref="Pixels"/> or <see cref="RawPixels"/> never pay for it.
    /// </summary>
    public byte[] PremultipliedPixels => _premultipliedPixels ??= ExpandToPremultipliedRgba();

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

    // Same math as AtlasPage.GetPremultipliedRgbaPixelData, but reading through Pitch —
    // keep the two in sync.
    private byte[] ExpandToPremultipliedRgba()
    {
        var rgba = new byte[Width * Height * 4];

        if (RawFormat == PixelFormat.Rgba32)
        {
            // Premultiply each pixel by its alpha, dropping any pitch slack.
            for (var row = 0; row < Height; row++)
            {
                for (var col = 0; col < Width; col++)
                {
                    var srcIdx = row * Pitch + col * 4;
                    if (srcIdx + 3 >= RawPixels.Length)
                        continue;

                    var dstIdx = (row * Width + col) * 4;
                    var a = RawPixels[srcIdx + 3];
                    if (a == 255)
                    {
                        rgba[dstIdx] = RawPixels[srcIdx];
                        rgba[dstIdx + 1] = RawPixels[srcIdx + 1];
                        rgba[dstIdx + 2] = RawPixels[srcIdx + 2];
                        rgba[dstIdx + 3] = 255;
                    }
                    else if (a != 0) // a == 0 stays transparent black (already zero)
                    {
                        rgba[dstIdx] = (byte)(RawPixels[srcIdx] * a / 255);
                        rgba[dstIdx + 1] = (byte)(RawPixels[srcIdx + 1] * a / 255);
                        rgba[dstIdx + 2] = (byte)(RawPixels[srcIdx + 2] * a / 255);
                        rgba[dstIdx + 3] = a;
                    }
                }
            }
            return rgba;
        }

        // Grayscale8 → (a, a, a, a): white coverage premultiplied by the coverage value.
        for (var row = 0; row < Height; row++)
        {
            for (var col = 0; col < Width; col++)
            {
                var srcIdx = row * Pitch + col;
                if (srcIdx >= RawPixels.Length)
                    continue;

                var a = RawPixels[srcIdx];
                var dstIdx = (row * Width + col) * 4;
                rgba[dstIdx] = a;
                rgba[dstIdx + 1] = a;
                rgba[dstIdx + 2] = a;
                rgba[dstIdx + 3] = a;
            }
        }
        return rgba;
    }
}
