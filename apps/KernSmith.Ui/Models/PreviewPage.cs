using KernSmith;
using KernSmith.Atlas;

namespace KernSmith.Ui.Models;

/// <summary>
/// One page of a generated bitmap font atlas, wrapping the library's
/// <see cref="AtlasPage"/> so the preview consumes the exact same pixel-format
/// helpers (<see cref="AtlasPage.GetRgbaPixelData"/>,
/// <see cref="AtlasPage.GetPremultipliedRgbaPixelData"/>) that downstream users do.
/// </summary>
public class PreviewPage
{
    /// <summary>The underlying library atlas page.</summary>
    public required AtlasPage Page { get; init; }

    /// <summary>Zero-based page index within the multi-page atlas.</summary>
    public int PageIndex { get; init; }

    /// <summary>Display label, e.g. "Page 0 (1024x1024)".</summary>
    public string Label { get; init; } = "";

    /// <summary>Atlas page width in pixels.</summary>
    public int Width => Page.Width;

    /// <summary>Atlas page height in pixels.</summary>
    public int Height => Page.Height;

    /// <summary>Whether this page uses RGBA32 format (true) or grayscale (false).</summary>
    public bool IsRgba => Page.Format == PixelFormat.Rgba32;

    /// <summary>Raw pixel data (RGBA32 or Grayscale8) in the page's native format.</summary>
    public byte[] PixelData => Page.PixelData;

    /// <summary>Pixel data as straight (non-premultiplied) RGBA32 — white-with-alpha for grayscale pages.</summary>
    public byte[] GetRgbaPixelData() => Page.GetRgbaPixelData();

    /// <summary>Pixel data as premultiplied RGBA32, ready for upload to a premultiplied-alpha blend state.</summary>
    public byte[] GetPremultipliedRgbaPixelData() => Page.GetPremultipliedRgbaPixelData();
}
