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
}
