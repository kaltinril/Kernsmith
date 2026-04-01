using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.DirectWrite.TerraFX;

/// <summary>
/// Describes DirectWrite rasterizer capabilities.
/// </summary>
internal sealed class DirectWriteCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => false;  // Stubbed only — no TranslateColorGlyphRun impl yet
    public bool SupportsVariableFonts => false;  // Stubbed only — no IDWriteFontFace5 axis impl yet
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool HandlesOwnSizing => false;
    public bool SupportsSystemFonts => true;
    public bool SupportsSyntheticBold => true;
    public bool SupportsSyntheticItalic => true;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
    [
        AntiAliasMode.None,
        AntiAliasMode.Grayscale
    ];
}
