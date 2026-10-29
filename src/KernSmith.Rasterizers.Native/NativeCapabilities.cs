using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports. It renders TrueType (<c>glyf</c>)
/// outlines with grayscale or thresholded coverage, including variable-font instances (171),
/// and color glyphs from <c>COLR</c> version 0 layers (Phase 172). The other optional features
/// are still <c>false</c> and expand as later phases land — synthetic bold/italic (167), outline
/// stroking (168) and SDF (169).
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    /// <summary>
    /// True: <c>COLR</c> version 0 layers with <c>CPAL</c> palettes. Version 1 paint graphs fall
    /// back to plain coverage and bitmap-only color fonts are rejected at load.
    /// </summary>
    public bool SupportsColorFonts => true;
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

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
