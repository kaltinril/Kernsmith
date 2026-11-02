using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>Axis helpers for fitting an outline's x axis with code written for its y axis.</summary>
internal static class OutlineAxes
{
    /// <summary>
    /// Swaps x and y of every point, so the outline's vertical features become horizontal ones.
    /// Its own inverse. Transposing mirrors the outline, which reverses every contour's
    /// winding; <see cref="GridFitter.FindEdges"/> takes the winding from the outline itself, so
    /// its "ink below the edge" reading stays right: in the transposed frame it means ink to the
    /// left of a vertical edge.
    /// </summary>
    public static GlyphOutline Transpose(GlyphOutline outline)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        var swapped = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            swapped[i] = new OutlineCommand(c.Type, c.EndY, c.EndX, c.Ctrl1Y, c.Ctrl1X, c.Ctrl2Y, c.Ctrl2X);
        }

        return new GlyphOutline(swapped, outline.YMin, outline.XMin, outline.YMax, outline.XMax);
    }

    /// <summary>A copy of the outline with y scaled about the baseline (y = 0).</summary>
    public static GlyphOutline ScaleY(GlyphOutline outline, float factor)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        var scaled = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            scaled[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndY = c.EndY * factor, Ctrl1Y = c.Ctrl1Y * factor, Ctrl2Y = c.Ctrl2Y * factor }
                : c with { EndY = c.EndY * factor };
        }

        return GlyphOutline.FromCommands(scaled);
    }

    /// <summary>
    /// A copy of the outline with the band from the baseline (y = 0) to <paramref name="xHeight"/>
    /// scaled by <paramref name="factor"/>, everything above it shifted by the band's growth,
    /// and everything below the baseline left where it was. A monotone map of y alone, so the
    /// outline cannot cross itself, and the caps, ascenders and descenders keep their own size
    /// while the x-height lands on its pixel line.
    /// </summary>
    public static GlyphOutline StretchBand(GlyphOutline outline, float xHeight, float factor)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        var scaled = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            scaled[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndY = Band(c.EndY, xHeight, factor), Ctrl1Y = Band(c.Ctrl1Y, xHeight, factor), Ctrl2Y = Band(c.Ctrl2Y, xHeight, factor) }
                : c with { EndY = Band(c.EndY, xHeight, factor) };
        }

        return GlyphOutline.FromCommands(scaled);
    }

    /// <summary>The x-height band stretch of one height, as <see cref="StretchBand"/> applies it.</summary>
    public static float Band(float y, float xHeight, float factor) =>
        y <= 0f ? y : y <= xHeight ? y * factor : y + (xHeight * (factor - 1f));

    /// <summary>A copy of the outline moved horizontally.</summary>
    public static GlyphOutline TranslateX(GlyphOutline outline, float dx)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        var moved = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            moved[i] = c.Type == OutlineCommandType.CubicTo
                ? c with { EndX = c.EndX + dx, Ctrl1X = c.Ctrl1X + dx, Ctrl2X = c.Ctrl2X + dx }
                : c with { EndX = c.EndX + dx };
        }

        return new GlyphOutline(moved, outline.XMin + dx, outline.YMin, outline.XMax + dx, outline.YMax);
    }
}
