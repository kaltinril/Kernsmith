using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports. It renders TrueType (<c>glyf</c>)
/// outlines with grayscale, thresholded or LCD subpixel coverage (173), including variable-font
/// instances (171).
/// The other optional features are still <c>false</c> and expand as later phases land —
/// synthetic bold/italic (167), outline stroking (168), SDF (169) and color fonts (172).
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => true;
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => false;
    public bool SupportsSyntheticItalic => false;

    /// <summary>
    /// False: the pipeline converts the requested BMFont cell height into an em size before
    /// calling in, so <see cref="RasterOptions.Size"/> arrives ready to use.
    /// </summary>
    public bool HandlesOwnSizing => false;

    /// <summary>
    /// <see cref="AntiAliasMode.Light"/> is left out until Phase 174 adds light hinting, so core
    /// negotiation degrades it to <see cref="AntiAliasMode.Grayscale"/>.
    /// </summary>
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Lcd];
}
