namespace KernSmith;

/// <summary>
/// Defines a rectangular region within an existing PNG image where glyphs will be rendered.
/// </summary>
public sealed class AtlasTargetRegion
{
    /// <summary>Path to the source PNG file. Either this or SourcePngData must be set.</summary>
    public string? SourcePngPath { get; set; }

    /// <summary>Raw PNG bytes of the source image. Either this or SourcePngPath must be set.</summary>
    public byte[]? SourcePngData { get; set; }

    /// <summary>X offset of the target region within the source image.</summary>
    public int X { get; set; }

    /// <summary>Y offset of the target region within the source image.</summary>
    public int Y { get; set; }

    /// <summary>Width of the target region in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height of the target region in pixels.</summary>
    public int Height { get; set; }
}
