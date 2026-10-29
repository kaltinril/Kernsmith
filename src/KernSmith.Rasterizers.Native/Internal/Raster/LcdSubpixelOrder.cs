namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>The physical left-to-right order of a display's colour subpixels.</summary>
internal enum LcdSubpixelOrder
{
    /// <summary>Red on the left, blue on the right — the common panel layout.</summary>
    Rgb,

    /// <summary>Blue on the left, red on the right.</summary>
    Bgr,
}
