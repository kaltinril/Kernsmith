using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.StbTrueType;

/// <summary>
/// Describes what the StbTrueType rasterizer backend supports. SDF generation and synthetic
/// bold/italic are supported; color fonts, variable fonts, and outline stroking are not.
/// TrueType (glyf) and CFF (.otf) outlines are handled; CFF2 is not.
/// </summary>
internal sealed class StbTrueTypeCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;
    public bool SupportsVariableFonts => false;
    public bool SupportsSdf => true;
    public bool SupportsOutlineStroke => false;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => true;
    public bool SupportsSyntheticItalic => true;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];
}
