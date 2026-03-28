using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.DirectWrite.TerraFX;

/// <summary>
/// Describes DirectWrite rasterizer capabilities.
/// </summary>
internal sealed class DirectWriteCapabilities : IRasterizerCapabilities
{
    public bool SupportsColorFonts => true;
    public bool SupportsVariableFonts => true;
    public bool SupportsSdf => false;
    public bool SupportsOutlineStroke => false;
    public bool HandlesOwnSizing => true;
    public bool SupportsSystemFonts => true;

    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
    [
        AntiAliasMode.None,
        AntiAliasMode.Grayscale
    ];
}
