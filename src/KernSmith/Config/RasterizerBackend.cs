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
    /// (<c>glyf</c>) and CFF/CFF2 (<c>.otf</c>) outlines, variable-font instances, <c>COLR</c>
    /// version 0 and 1 color glyphs and <c>CBDT</c>/<c>sbix</c> color bitmaps, synthetic
    /// bold/italic, SDF and multi-channel MSDF/MTSDF, with <see cref="AntiAliasMode.None"/>,
    /// <see cref="AntiAliasMode.Grayscale"/>, <see cref="AntiAliasMode.Light"/> (its own
    /// vertical-only auto-hinting) and <see cref="AntiAliasMode.Lcd"/> anti-aliasing. It is the
    /// only backend that draws <see cref="OutlineMode.Vector"/> outlines and honors
    /// <see cref="FontGeneratorOptions.WidthPercent"/> and <see cref="FontGeneratorOptions.ItalicAngle"/>.
    /// It is supplied by the
    /// <c>KernSmith.Rasterizers.Native</c> project, which currently ships only in the KernSmith
    /// repository and CLI. Until that package is released,
    /// <see cref="KernSmith.Rasterizer.RasterizerFactory.Create"/> throws for this value in apps
    /// built against the NuGet packages.
    /// </para>
    /// <para>
    /// Limitations: no TrueType bytecode hinting (and no hinting outside <c>Light</c>) and no
    /// system-font lookup. Use <see cref="FreeType"/> for hinting, <see cref="Gdi"/> or
    /// <see cref="DirectWrite"/> for system fonts.
    /// </para>
    /// </summary>
    Native
}
