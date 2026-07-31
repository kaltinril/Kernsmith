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
    /// <b>Not usable yet.</b> This backend is still under development: it has no published
    /// NuGet package, so it can never be resolved from a released build and
    /// <see cref="KernSmith.Rasterizer.RasterizerFactory.Create"/> will throw for it.
    /// The value is reserved so the enum does not change shape when the backend ships.
    /// Use <see cref="FreeType"/>, <see cref="Gdi"/>, <see cref="DirectWrite"/>, or
    /// <see cref="StbTrueType"/> instead.
    /// </para>
    /// </summary>
    Native
}
