namespace Bmfontier;

/// <summary>
/// Texture format for atlas output images.
/// </summary>
public enum TextureFormat
{
    /// <summary>PNG format (default).</summary>
    Png = 0,

    /// <summary>TGA (Targa) format — uncompressed, simple header + raw pixels.</summary>
    Tga = 1,

    /// <summary>DDS (DirectDraw Surface) format — uncompressed, for DirectX-based engines.</summary>
    Dds = 2
}
