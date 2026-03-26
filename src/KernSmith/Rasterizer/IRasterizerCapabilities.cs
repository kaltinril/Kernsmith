namespace KernSmith.Rasterizer;

/// <summary>
/// Describes what a rasterizer backend supports.
/// </summary>
public interface IRasterizerCapabilities
{
    /// <summary>Whether the rasterizer can render color font glyphs (COLR/CPAL/sbix/CBDT).</summary>
    bool SupportsColorFonts { get; }

    /// <summary>Whether the rasterizer can apply variable font axis coordinates.</summary>
    bool SupportsVariableFonts { get; }

    /// <summary>Whether the rasterizer can produce signed distance field output.</summary>
    bool SupportsSdf { get; }

    /// <summary>Whether the rasterizer can stroke glyph outlines.</summary>
    bool SupportsOutlineStroke { get; }

    /// <summary>The anti-alias modes this rasterizer supports.</summary>
    IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; }
}
