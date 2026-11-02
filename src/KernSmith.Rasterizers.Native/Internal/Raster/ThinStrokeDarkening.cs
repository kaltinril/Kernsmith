using System.Buffers;

namespace KernSmith.Rasterizers.Native.Internal.Raster;

/// <summary>
/// Darkens thin strokes in small grayscale text. The <c>Normal</c> fitter lands a stem under
/// a pixel wide in one column at its true coverage, so at 6-8 ppem a 0.55 px stem is a 55%
/// grey bar and a line of text reads as mid-grey. This lifts the coverage of every partially
/// covered pixel that is <em>isolated across one axis</em> — both its horizontal neighbours,
/// or both its vertical ones, hold next to no ink — which is a thin stem, bar, diagonal or
/// curve, and leaves alone the anti-aliased fringe beside a solid pixel, which is the edge of
/// a wider stroke. The lift tapers from <see cref="MaxBoost"/> at <see cref="FullPpem"/> to
/// nothing at <see cref="EndPpem"/>; above that strokes are a pixel or more and need none.
/// No geometry moves: boxes, advances and the set of lit pixels are unchanged.
/// </summary>
internal static class ThinStrokeDarkening
{
    /// <summary>
    /// At and above this size nothing is darkened: by 9 ppem a text face's stems are 0.8 px
    /// and more, which the fitter already lands in one column at 80%+ coverage.
    /// </summary>
    public const float EndPpem = 9f;

    /// <summary>At and below this size the lift is <see cref="MaxBoost"/>.</summary>
    public const float FullPpem = 6f;

    /// <summary>The largest lift, as a fraction of the coverage: 0.4 takes a 0.55 px stem from 140 to 196.</summary>
    public const float MaxBoost = 0.4f;

    /// <summary>A neighbour at or under this coverage holds next to no ink.</summary>
    private const byte Faint = 64;

    /// <summary>The factor coverage of a thin stroke is multiplied by at a size: 1 at <see cref="EndPpem"/> and above.</summary>
    public static float Factor(float ppem) =>
        1f + (MaxBoost * Math.Clamp((EndPpem - ppem) / (EndPpem - FullPpem), 0f, 1f));

    /// <summary>Lifts the thin-stroke pixels of a coverage bitmap in place; a no-op at <see cref="EndPpem"/> and above.</summary>
    public static void Apply(Span<byte> coverage, int width, int height, float ppem)
    {
        float factor = Factor(ppem);
        if (factor <= 1f || width <= 0 || height <= 0)
            return;

        // Neighbours are read from the input, not from pixels already lifted.
        int size = width * height;
        byte[] rented = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var source = rented.AsSpan(0, size);
            coverage.CopyTo(source);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x;
                    byte c = source[i];
                    if (c == 0 || c == 255)
                        continue;

                    bool acrossX = At(source, width, height, x - 1, y) <= Faint && At(source, width, height, x + 1, y) <= Faint;
                    bool acrossY = At(source, width, height, x, y - 1) <= Faint && At(source, width, height, x, y + 1) <= Faint;
                    if (acrossX || acrossY)
                        coverage[i] = (byte)Math.Min(255f, MathF.Round(c * factor));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static byte At(ReadOnlySpan<byte> source, int width, int height, int x, int y) =>
        x < 0 || y < 0 || x >= width || y >= height ? (byte)0 : source[(y * width) + x];
}
