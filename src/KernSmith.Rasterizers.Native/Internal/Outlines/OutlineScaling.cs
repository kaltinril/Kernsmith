namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>Axis-aligned scaling of an outline about the origin (pen position on the baseline).</summary>
internal static class OutlineScaling
{
    /// <summary>Scales every point, cubic controls included, by (<paramref name="sx"/>, <paramref name="sy"/>).</summary>
    public static GlyphOutline Scale(GlyphOutline outline, float sx, float sy)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.IsEmpty)
            return outline;

        var source = outline.Commands;
        var scaled = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            scaled[i] = c with
            {
                EndX = c.EndX * sx,
                EndY = c.EndY * sy,
                Ctrl1X = c.Ctrl1X * sx,
                Ctrl1Y = c.Ctrl1Y * sy,
                Ctrl2X = c.Ctrl2X * sx,
                Ctrl2Y = c.Ctrl2Y * sy,
            };
        }

        return GlyphOutline.FromCommands(scaled);
    }
}
