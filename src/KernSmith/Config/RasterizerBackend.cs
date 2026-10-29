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
    DirectWrite,

    /// <summary>StbTrueType rasterizer. Pure C#, cross-platform. No native dependencies.</summary>
    StbTrueType,

    /// <summary>
    /// Native KernSmith rasterizer. Pure C#, cross-platform, no external dependencies.
    /// <para>
    /// <b>Experimental, and not published to NuGet yet.</b> The backend renders TrueType
    /// (<c>glyf</c>) and CFF/CFF2 (<c>.otf</c>) outlines and is supplied by the <c>KernSmith.Rasterizers.Native</c>
    /// project, which currently ships only in the KernSmith repository and CLI. Until that
    /// package is released, <see cref="KernSmith.Rasterizer.RasterizerFactory.Create"/>
    /// throws for this value in apps built against the NuGet packages.
    /// </para>
    /// <para>
    /// Limitations: no hinting, outline stroking or system-font lookup, color fonts from
    /// <c>COLR</c> version 0 layers only, and no <see cref="AntiAliasMode.Light"/> anti-aliasing
    /// (it renders as grayscale). Use <see cref="FreeType"/> or <see cref="StbTrueType"/> for those.
    /// </para>
    /// </summary>
    Native
}
