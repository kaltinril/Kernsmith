namespace KernSmith;

/// <summary>
/// Which rasterizer backend to use for glyph rendering.
/// </summary>
public enum RasterizerBackend
{
    /// <summary>FreeType rasterizer. Cross-platform, full-featured.</summary>
    FreeType,

    /// <summary>GDI rasterizer. Windows-only.</summary>
    Gdi,

    /// <summary>DirectWrite rasterizer. Windows-only, high quality.</summary>
    DirectWrite
}
