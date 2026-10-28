using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports. It renders TrueType (<c>glyf</c>)
/// outlines with grayscale or thresholded coverage, and applies synthetic bold/italic as outline
/// transforms (Phase 167). The other optional features are still <c>false</c> and expand as
/// later phases land — outline stroking (168), SDF (169), variable fonts (171) and color fonts
/// (172).
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => true;
    public bool SupportsSyntheticItalic => true;

    /// <summary>
    /// False: the pipeline converts the requested BMFont cell height into an em size before
    /// calling in, so <see cref="RasterOptions.Size"/> arrives ready to use.
    /// </summary>
    public bool HandlesOwnSizing => false;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
