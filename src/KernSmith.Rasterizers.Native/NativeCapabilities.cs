using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports: TrueType (<c>glyf</c>) outlines with
/// grayscale or thresholded coverage, including variable-font instances (171), single-channel
/// signed distance fields (169) and multi-channel MSDF/MTSDF (170). The other optional
/// features are still <c>false</c> and expand as later phases land — synthetic bold/italic
/// (167), outline stroking (168) and color fonts (172).
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => true;
    public bool SupportsSdf => true;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => false;
    public bool SupportsSyntheticItalic => false;
    public bool SupportsMsdf => true;

    /// <summary>
    /// False: the pipeline converts the requested BMFont cell height into an em size before
    /// calling in, so <see cref="RasterOptions.Size"/> arrives ready to use.
    /// </summary>
    public bool HandlesOwnSizing => false;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
