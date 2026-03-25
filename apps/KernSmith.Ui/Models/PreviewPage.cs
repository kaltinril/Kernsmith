namespace KernSmith.Ui.Models;

/// <summary>
/// One page of a generated bitmap font atlas, carrying the PNG image data
/// for display in the preview panel.
/// </summary>
public class PreviewPage
{
    /// <summary>Zero-based page index within the multi-page atlas.</summary>
    public int PageIndex { get; init; }
    /// <summary>Raw PNG-encoded image bytes for this atlas page.</summary>
    public byte[] PngData { get; init; } = Array.Empty<byte>();
    /// <summary>Atlas page width in pixels.</summary>
    public int Width { get; init; }
    /// <summary>Atlas page height in pixels.</summary>
    public int Height { get; init; }
    /// <summary>Display label, e.g. "Page 0 (1024x1024)".</summary>
    public string Label { get; init; } = "";
    /// <summary>Whether this page uses RGBA32 format (true) or grayscale (false).</summary>
    public bool IsRgba { get; init; }
}
