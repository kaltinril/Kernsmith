namespace KernSmith;

/// <summary>
/// The physical layout of a display's colour subpixels, for <see cref="AntiAliasMode.Lcd"/>
/// rendering. The names are fontconfig's <c>rgba</c> values. Only backends that render subpixel
/// output honour it (the Native backend); the others ignore it.
/// </summary>
public enum LcdSubpixelOrder
{
    /// <summary>Horizontal stripes, red on the left and blue on the right — the common panel layout (default).</summary>
    Rgb,

    /// <summary>Horizontal stripes, blue on the left and red on the right: <see cref="Rgb"/> with red and blue swapped.</summary>
    Bgr,

    /// <summary>
    /// Vertical stripes (a panel rotated a quarter turn), red on top and blue at the bottom.
    /// Subpixel resolution is vertical: <see cref="Rgb"/> with rows and columns exchanged.
    /// </summary>
    VRgb,

    /// <summary>Vertical stripes, blue on top and red at the bottom: <see cref="VRgb"/> with red and blue swapped.</summary>
    VBgr,
}
