namespace KernSmith;

/// <summary>
/// How glyphs are anti-aliased when rendered. A backend that does not support the requested
/// mode (see <see cref="Rasterizer.IRasterizerCapabilities.SupportedAntiAliasModes"/>) renders
/// <see cref="Grayscale"/> instead.
/// </summary>
public enum AntiAliasMode
{
    /// <summary>No anti-aliasing. Pixels are fully on or fully off.</summary>
    None,

    /// <summary>Smooth grayscale edges (default). Works everywhere.</summary>
    Grayscale,

    /// <summary>Light hinting for smoother curves at small sizes.</summary>
    Light,

    /// <summary>LCD sub-pixel rendering. Only useful for desktop screens, not game textures.</summary>
    Lcd
}
