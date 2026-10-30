namespace KernSmith;

/// <summary>
/// The kind of distance field to generate instead of a coverage bitmap. Selected through
/// <see cref="FontGeneratorOptions.SdfMode"/>; the older <see cref="FontGeneratorOptions.Sdf"/>
/// flag still means <see cref="Sdf"/> when this is left at <see cref="None"/>.
/// </summary>
public enum SdfMode
{
    /// <summary>No distance field, unless <see cref="FontGeneratorOptions.Sdf"/> is set (default).</summary>
    None,

    /// <summary>Single-channel signed distance field: one 8-bit distance per texel, inside bright, 128 on the edge.</summary>
    Sdf,

    /// <summary>
    /// Multi-channel signed distance field: RGB carry three distances whose median is the shape,
    /// keeping corners sharp under magnification; alpha is 255. Output is RGBA. Requires a
    /// backend reporting <see cref="Rasterizer.IRasterizerCapabilities.SupportsMsdf"/>.
    /// </summary>
    Msdf,

    /// <summary>
    /// Multi-channel and true signed distance field: <see cref="Msdf"/>'s RGB plus the
    /// single-channel <see cref="Sdf"/> distance in alpha. Output is RGBA. Requires a backend
    /// reporting <see cref="Rasterizer.IRasterizerCapabilities.SupportsMsdf"/>.
    /// </summary>
    Mtsdf
}
