namespace KernSmith.Rasterizer;

/// <summary>
/// Font-wide metrics provided by a rasterizer backend.
/// </summary>
public sealed record RasterizerFontMetrics
{
    /// <summary>Ascent in pixels (distance from baseline to top of highest glyph).</summary>
    public required int Ascent { get; init; }
    /// <summary>Descent in pixels (distance from baseline to bottom of lowest glyph).</summary>
    public required int Descent { get; init; }
    /// <summary>Line height in pixels (typically Ascent + Descent).</summary>
    public required int LineHeight { get; init; }

    /// <summary>
    /// Relative slack under which a scaled metric counts as a whole pixel count. A
    /// <see cref="RasterOptions.Size"/> is a <see cref="float"/> (relative error up to 2^-24, about
    /// 6e-8), and backends that scale in single precision add a few more such roundings; one part per
    /// million clears that noise with a wide margin while staying far below a visible fraction of a
    /// pixel (0.001 px on a 1000 px line box).
    /// </summary>
    private const double WholePixelTolerance = 1e-6;

    /// <summary>
    /// Rounds a font metric scaled to pixels (a line height, ascent or descent) up to whole pixels,
    /// treating a value within floating-point noise of an integer as that integer.
    /// </summary>
    /// <remarks>
    /// A line box rounds up, not to nearest, so it never clips the glyphs it is meant to contain.
    /// But with BMFont cell sizing the requested size becomes a fractional ppem
    /// (<c>size × unitsPerEm / (winAscent + winDescent)</c>), so the win box scaled back from it is
    /// exactly <c>size</c> in theory and a hair above or below it in practice; a plain ceiling then
    /// turns the hair above into <c>size + 1</c>. Backends implementing
    /// <see cref="IRasterizer.GetFontMetrics"/> should round their scaled metrics through this method
    /// so every backend reports the same line box.
    /// </remarks>
    /// <param name="pixels">The scaled metric, in pixels.</param>
    /// <returns>The smallest whole pixel count not below <paramref name="pixels"/>, allowing for float noise.</returns>
    public static int CeilingToPixels(double pixels)
    {
        double nearest = Math.Round(pixels);
        if (Math.Abs(pixels - nearest) <= Math.Abs(pixels) * WholePixelTolerance)
            return (int)nearest;

        return (int)Math.Ceiling(pixels);
    }
}
