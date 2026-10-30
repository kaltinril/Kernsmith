using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Hinting;

/// <summary>Small numeric helpers shared by the hinting code.</summary>
internal static class GridMath
{
    /// <summary>Rounds half up, so values on either side of zero round the same way.</summary>
    public static float Round(float value) => MathF.Floor(value + 0.5f);

    /// <summary>A uniformly scaled copy of an outline, Y still up.</summary>
    public static GlyphOutline ScaleOutline(GlyphOutline outline, float scale)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var source = outline.Commands;
        var scaled = new OutlineCommand[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var c = source[i];
            scaled[i] = c with
            {
                EndX = c.EndX * scale,
                EndY = c.EndY * scale,
                Ctrl1X = c.Ctrl1X * scale,
                Ctrl1Y = c.Ctrl1Y * scale,
                Ctrl2X = c.Ctrl2X * scale,
                Ctrl2Y = c.Ctrl2Y * scale,
            };
        }

        return GlyphOutline.FromCommands(scaled);
    }
}
