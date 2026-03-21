namespace KernSmith.Rasterizer;

/// <summary>
/// Post-processor that applies a color gradient to glyph bitmaps at any angle,
/// converting them from grayscale to RGBA.
/// </summary>
public sealed class GradientPostProcessor : IGlyphPostProcessor
{
    public byte StartR { get; }
    public byte StartG { get; }
    public byte StartB { get; }
    public byte EndR { get; }
    public byte EndG { get; }
    public byte EndB { get; }

    /// <summary>
    /// Gradient angle in degrees. 0 = left-to-right, 90 = top-to-bottom,
    /// 180 = right-to-left, 270 = bottom-to-top.
    /// </summary>
    public float AngleDegrees { get; }

    /// <summary>
    /// Controls where the midpoint of the gradient falls (0.0 to 1.0).
    /// Default 0.5 = even blend. Lower values push the start color further
    /// (e.g., 0.1 = 90% start color, 10% transition to end color).
    /// Higher values push the end color further
    /// (e.g., 0.9 = 10% transition from start, 90% end color).
    /// </summary>
    public float Midpoint { get; }

    public GradientPostProcessor(
        byte startR, byte startG, byte startB,
        byte endR, byte endG, byte endB,
        float angleDegrees = 90f,
        float midpoint = 0.5f)
    {
        StartR = startR; StartG = startG; StartB = startB;
        EndR = endR; EndG = endG; EndB = endB;
        AngleDegrees = angleDegrees;
        Midpoint = Math.Clamp(midpoint, 0.01f, 0.99f);
    }

    /// <summary>
    /// Creates a gradient with configurable angle and midpoint.
    /// </summary>
    public static GradientPostProcessor Create(
        (byte R, byte G, byte B) startColor,
        (byte R, byte G, byte B) endColor,
        float angleDegrees = 90f,
        float midpoint = 0.5f)
        => new(startColor.R, startColor.G, startColor.B,
               endColor.R, endColor.G, endColor.B,
               angleDegrees, midpoint);

    public RasterizedGlyph Process(RasterizedGlyph glyph)
    {
        // Skip RGBA glyphs (e.g., color emoji) — they already have color data.
        if (glyph.Format == PixelFormat.Rgba32)
            return glyph;

        if (glyph.Width == 0 || glyph.Height == 0)
            return glyph;

        var rgba = new byte[glyph.Width * glyph.Height * 4];

        // Compute direction vector from angle
        var radians = AngleDegrees * MathF.PI / 180f;
        var dirX = MathF.Cos(radians);
        var dirY = MathF.Sin(radians);

        // Project all four corners onto the direction vector to find the range
        // Corners: (0,0), (w-1,0), (0,h-1), (w-1,h-1)
        float w = glyph.Width - 1;
        float h = glyph.Height - 1;
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        float d00 = 0;
        float d10 = w * dirX;
        float d01 = h * dirY;
        float d11 = w * dirX + h * dirY;

        float minD = MathF.Min(MathF.Min(d00, d10), MathF.Min(d01, d11));
        float maxD = MathF.Max(MathF.Max(d00, d10), MathF.Max(d01, d11));
        float range = maxD - minD;
        if (range < 0.001f) range = 1f;

        for (var y = 0; y < glyph.Height; y++)
        {
            for (var x = 0; x < glyph.Width; x++)
            {
                // Project pixel onto gradient direction and normalize to 0..1
                float dot = x * dirX + y * dirY;
                float t = (dot - minD) / range;
                t = MathF.Max(0f, MathF.Min(1f, t));

                // Apply midpoint bias: remap so t=Midpoint becomes the 50% blend point
                // This uses a simple piecewise linear remap
                if (t < Midpoint)
                    t = t / Midpoint * 0.5f;
                else
                    t = 0.5f + (t - Midpoint) / (1f - Midpoint) * 0.5f;

                var r = (byte)(StartR + (EndR - StartR) * t);
                var g = (byte)(StartG + (EndG - StartG) * t);
                var b = (byte)(StartB + (EndB - StartB) * t);

                var srcIdx = y * glyph.Pitch + x;
                var alpha = glyph.BitmapData[srcIdx];

                var dstIdx = (y * glyph.Width + x) * 4;
                rgba[dstIdx + 0] = r;
                rgba[dstIdx + 1] = g;
                rgba[dstIdx + 2] = b;
                rgba[dstIdx + 3] = alpha;
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = glyph.Codepoint,
            GlyphIndex = glyph.GlyphIndex,
            BitmapData = rgba,
            Width = glyph.Width,
            Height = glyph.Height,
            Pitch = glyph.Width * 4,
            Metrics = glyph.Metrics,
            Format = PixelFormat.Rgba32
        };
    }
}
