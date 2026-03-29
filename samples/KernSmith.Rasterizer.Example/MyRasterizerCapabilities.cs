using KernSmith;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.MyRasterizer;

/// <summary>
/// Describes what the MyRasterizer backend supports.
/// Return true for each capability your rasterizer implements.
/// </summary>
public sealed class MyRasterizerCapabilities : IRasterizerCapabilities
{
    /// <summary>Whether this rasterizer can render color font glyphs (COLR/CPAL/sbix/CBDT).</summary>
    public bool SupportsColorFonts => false;

    /// <summary>Whether this rasterizer can apply variable font axis coordinates.</summary>
    public bool SupportsVariableFonts => false;

    /// <summary>Whether this rasterizer can produce signed distance field output.</summary>
    public bool SupportsSdf => false;

    /// <summary>Whether this rasterizer can stroke glyph outlines.</summary>
    public bool SupportsOutlineStroke => false;

    /// <summary>The anti-alias modes this rasterizer supports.</summary>
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale];

    // Optional: uncomment and return true if your rasterizer handles font sizing
    // internally (bypasses KernSmith's cell-height-to-ppem conversion).
    // public bool HandlesOwnSizing => true;

    // Optional: uncomment and return true if your rasterizer can load system-installed
    // fonts by family name via LoadSystemFont().
    // public bool SupportsSystemFonts => true;
}
