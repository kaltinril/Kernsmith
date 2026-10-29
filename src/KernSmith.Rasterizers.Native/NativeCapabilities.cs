using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports: TrueType (<c>glyf</c>) and CFF/CFF2
/// outlines with grayscale, thresholded or LCD subpixel (173) coverage, including
/// variable-font instances of both (171), color glyphs from <c>COLR</c> version 0 layers and
/// version 1 paint graphs (172), synthetic bold/italic as outline transforms (167),
/// single-channel signed distance fields (169) and multi-channel MSDF/MTSDF (170).
/// Outline stroking (168) is still <c>false</c>.
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    /// <summary>
    /// True: <c>COLR</c> version 0 layers and version 1 paint graphs with <c>CPAL</c> palettes.
    /// Bitmap-only color fonts are rejected at load.
    /// </summary>
    public bool SupportsColorFonts => true;
    public bool SupportsVariableFonts => true;
    public bool SupportsSdf => true;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => true;
    public bool SupportsSyntheticItalic => true;
    public bool SupportsMsdf => true;

    /// <summary>
    /// False: the pipeline converts the requested BMFont cell height into an em size before
    /// calling in, so <see cref="RasterOptions.Size"/> arrives ready to use.
    /// </summary>
    public bool HandlesOwnSizing => false;

    /// <summary>
    /// <see cref="AntiAliasMode.Light"/> is grayscale with Native's light (vertical-only)
    /// auto-hinting when <see cref="RasterOptions.EnableHinting"/> is on (Phase 174).
    /// </summary>
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Light, AntiAliasMode.Lcd];
}
