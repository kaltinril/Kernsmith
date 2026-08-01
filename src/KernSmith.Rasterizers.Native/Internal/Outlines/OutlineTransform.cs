namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// Maps font design units to bitmap pixel space: a uniform scale plus the Y-axis flip from
/// TrueType's Y-up coordinates to a top-down bitmap.
/// </summary>
/// <param name="Scale">Pixels per font unit, i.e. <c>pixelSize / unitsPerEm</c>.</param>
/// <param name="AscentPixels">
/// Distance in pixels from the top of the coordinate space down to the baseline. Font-unit
/// Y values are subtracted from it, so the baseline (y = 0) lands on this row.
/// </param>
internal readonly record struct OutlineTransform(float Scale, float AscentPixels)
{
    /// <summary>
    /// Builds the transform for a given pixel size.
    /// </summary>
    /// <param name="pixelSize">Em size in pixels.</param>
    /// <param name="unitsPerEm">The font's <c>head.unitsPerEm</c>.</param>
    /// <param name="ascentFontUnits">The ascent to place the baseline against, in font units.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="unitsPerEm"/> is not positive.</exception>
    public static OutlineTransform Create(float pixelSize, int unitsPerEm, float ascentFontUnits)
    {
        if (unitsPerEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerEm), unitsPerEm, "unitsPerEm must be positive.");

        float scale = pixelSize / unitsPerEm;
        return new OutlineTransform(scale, ascentFontUnits * scale);
    }

    /// <summary>Converts a font-unit X coordinate to pixels.</summary>
    public float ToPixelX(float x) => x * Scale;

    /// <summary>Converts a font-unit Y coordinate to pixels, flipping the axis.</summary>
    public float ToPixelY(float y) => AscentPixels - (y * Scale);
}
