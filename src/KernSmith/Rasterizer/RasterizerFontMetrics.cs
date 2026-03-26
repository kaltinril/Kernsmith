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
}
