using System;

namespace KernSmith.Atlas;

/// <summary>
/// A single texture page in the atlas containing packed glyph pixel data.
/// </summary>
public sealed class AtlasPage
{
    /// <summary>Page number (zero-based).</summary>
    public required int PageIndex { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Raw pixel data in row-major order, top-to-bottom.
    /// Format depends on <see cref="Format"/>:
    /// <list type="bullet">
    /// <item><description><see cref="PixelFormat.Rgba32"/>: 4 bytes per pixel (R, G, B, A), width * height * 4 bytes total.</description></item>
    /// <item><description><see cref="PixelFormat.Grayscale8"/>: 1 byte per pixel (alpha/luminance), width * height bytes total.</description></item>
    /// </list>
    /// Suitable for direct upload to GPU textures (e.g., MonoGame Texture2D.SetData).
    /// </summary>
    public required byte[] PixelData { get; init; }

    /// <summary>Whether the pixel data is grayscale, RGBA, etc.</summary>
    public required PixelFormat Format { get; init; }

    private IAtlasEncoder? _encoder;

    internal void SetEncoder(IAtlasEncoder encoder) => _encoder = encoder;

    /// <summary>
    /// Encodes this page to PNG bytes. Throws if no encoder is configured.
    /// </summary>
    /// <returns>PNG-encoded byte array.</returns>
    public byte[] ToPng() =>
        (_encoder ?? throw new InvalidOperationException("No encoder configured"))
            .Encode(PixelData, Width, Height, Format);

    /// <summary>Encodes this atlas page as a TGA image.</summary>
    /// <returns>The TGA file bytes.</returns>
    public byte[] ToTga() =>
        new TgaEncoder().Encode(PixelData, Width, Height, Format);

    /// <summary>Encodes this atlas page as a DDS image.</summary>
    /// <returns>The DDS file bytes.</returns>
    public byte[] ToDds() =>
        new DdsEncoder().Encode(PixelData, Width, Height, Format);

    /// <summary>
    /// Returns pixel data as RGBA32 bytes regardless of the page's native format.
    /// Grayscale pages expand each byte to (255, 255, 255, v) — the canonical
    /// Angelcode BMFont alpha-coverage layout where RGB is white and the original
    /// grayscale value becomes the alpha channel. RGBA pages return a copy of
    /// <see cref="PixelData"/> unchanged. The returned array is always a fresh
    /// buffer owned by the caller; mutating it does not affect <see cref="PixelData"/>.
    /// </summary>
    /// <returns>A byte array of length Width * Height * 4 in RGBA order (R, G, B, A).</returns>
    public byte[] GetRgbaPixelData()
    {
        if (Format == PixelFormat.Rgba32)
            return (byte[])PixelData.Clone();

        // PixelFormat.Grayscale8 → expand to (255, 255, 255, v)
        var rgba = new byte[Width * Height * 4];
        for (int i = 0; i < PixelData.Length; i++)
        {
            int j = i * 4;
            rgba[j] = 255;
            rgba[j + 1] = 255;
            rgba[j + 2] = 255;
            rgba[j + 3] = PixelData[i];
        }
        return rgba;
    }

    /// <summary>
    /// Returns pixel data as 8-bit alpha coverage bytes regardless of the page's
    /// native format. RGBA pages collapse to the alpha channel only (one byte per
    /// pixel). Grayscale pages return a copy of <see cref="PixelData"/> unchanged.
    /// The returned array is always a fresh buffer owned by the caller; mutating
    /// it does not affect <see cref="PixelData"/>.
    /// </summary>
    /// <returns>A byte array of length Width * Height where each byte is alpha coverage (0..255).</returns>
    public byte[] GetAlpha8PixelData()
    {
        if (Format == PixelFormat.Grayscale8)
            return (byte[])PixelData.Clone();

        // PixelFormat.Rgba32 → extract alpha channel
        var alpha = new byte[Width * Height];
        for (int i = 0; i < alpha.Length; i++)
            alpha[i] = PixelData[i * 4 + 3];
        return alpha;
    }
}
