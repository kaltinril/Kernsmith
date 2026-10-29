namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>The alignment heights the auto-hinter snaps horizontal edges to.</summary>
internal enum BlueZoneKind
{
    /// <summary>The baseline; round glyphs overshoot below it.</summary>
    Baseline,

    /// <summary>The top of flat lowercase letters ('x'); round ones ('o') overshoot above it.</summary>
    XHeight,

    /// <summary>The top of flat capitals ('H'); round ones ('O') overshoot above it.</summary>
    CapHeight,

    /// <summary>The top of lowercase ascenders ('d').</summary>
    Ascender,

    /// <summary>The bottom of lowercase descenders ('p').</summary>
    Descender,
}

/// <summary>
/// One alignment zone in font units: the height flat-topped (or flat-bottomed) glyphs share,
/// and the height their round counterparts overshoot to. For the top zones
/// <see cref="Overshoot"/> is at or above <see cref="Flat"/>; for the bottom zones (baseline,
/// descender) at or below it. A zone with no measurable overshoot has both equal.
/// </summary>
internal readonly record struct BlueZone(BlueZoneKind Kind, float Flat, float Overshoot)
{
    /// <summary>True for zones that align the tops of glyphs; false for baseline and descender.</summary>
    public bool IsTop => Kind is BlueZoneKind.XHeight or BlueZoneKind.CapHeight or BlueZoneKind.Ascender;
}

/// <summary>
/// A blue zone at one pixel size, Y up: the scaled flat and overshoot heights and the pixel
/// lines they snap to.
/// </summary>
/// <param name="IsTop">True for zones that align the tops of glyphs.</param>
/// <param name="Flat">The scaled flat height, in pixels.</param>
/// <param name="Overshoot">The scaled overshoot height, in pixels.</param>
/// <param name="SnappedFlat">The whole-pixel line flat edges snap to.</param>
/// <param name="SnappedOvershoot">The whole-pixel line overshooting edges snap to — the same as
/// <paramref name="SnappedFlat"/> while the overshoot is under half a pixel.</param>
internal readonly record struct PixelZone(bool IsTop, float Flat, float Overshoot, float SnappedFlat, float SnappedOvershoot);
