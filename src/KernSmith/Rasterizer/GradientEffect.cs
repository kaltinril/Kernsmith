using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

/// <summary>
/// Generates a colored body layer by applying a gradient to a grayscale alpha mask.
/// Z-order 2: renders on top (body layer).
/// </summary>
internal sealed class GradientEffect : IGlyphEffect
{
    public int ZOrder => 2;

    private readonly byte _startR, _startG, _startB;
    private readonly byte _endR, _endG, _endB;
    private readonly float _angleDegrees;
    private readonly float _midpoint;

    public GradientEffect(
        byte startR, byte startG, byte startB,
        byte endR, byte endG, byte endB,
        float angleDegrees = 90f,
        float midpoint = 0.5f)
    {
        _startR = startR; _startG = startG; _startB = startB;
        _endR = endR; _endG = endG; _endB = endB;
        _angleDegrees = angleDegrees;
        _midpoint = Math.Clamp(midpoint, 0.01f, 0.99f);
    }

    public GlyphLayer Generate(byte[] alphaData, int width, int height, int pitch, GlyphMetrics metrics)
    {
        var rgba = new byte[width * height * 4];

        // Compute direction vector from angle
        var radians = _angleDegrees * MathF.PI / 180f;
        var dirX = MathF.Cos(radians);
        var dirY = MathF.Sin(radians);

        // Project all four corners onto the direction vector to find the range
        float w = width - 1;
        float h = height - 1;
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

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Project pixel onto gradient direction and normalize to 0..1
                float dot = x * dirX + y * dirY;
                float t = (dot - minD) / range;
                t = MathF.Max(0f, MathF.Min(1f, t));

                // Apply midpoint bias
                if (t < _midpoint)
                    t = t / _midpoint * 0.5f;
                else
                    t = 0.5f + (t - _midpoint) / (1f - _midpoint) * 0.5f;

                var r = (byte)(_startR + (_endR - _startR) * t);
                var g = (byte)(_startG + (_endG - _startG) * t);
                var b = (byte)(_startB + (_endB - _startB) * t);

                var srcIdx = y * pitch + x;
                var alpha = srcIdx < alphaData.Length ? alphaData[srcIdx] : (byte)0;

                var dstIdx = (y * width + x) * 4;
                rgba[dstIdx + 0] = r;
                rgba[dstIdx + 1] = g;
                rgba[dstIdx + 2] = b;
                rgba[dstIdx + 3] = alpha;
            }
        }

        return new GlyphLayer(rgba, width, height, OffsetX: 0, OffsetY: 0, ZOrder);
    }
}
