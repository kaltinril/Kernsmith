using KernSmith.Atlas;
using KernSmith.Output.Model;

namespace KernSmith.Ui.Models;

/// <summary>
/// Turns a distance-field atlas page into the coverage it encodes, so the Sample tab shows
/// glyphs rather than raw distances (an MSDF page's RGB channels are distances, not color).
/// </summary>
public static class DistanceFieldPreview
{
    /// <summary>
    /// Returns premultiplied RGBA white-with-coverage pixels for a distance-field page, or null
    /// when the page is not a distance field and should be shown as is.
    /// </summary>
    public static byte[]? DecodeToCoverage(AtlasPage page, ExtendedMetadata? extended)
    {
        if (extended?.SdfSpread is not { } spreadPx || spreadPx <= 0)
            return null;

        var multiChannel = extended.SdfMode is SdfMode.Msdf or SdfMode.Mtsdf;
        // Single-channel SDF with color glyphs is an RGBA page whose color glyphs stay color.
        if (multiChannel ? page.Format != PixelFormat.Rgba32 : page.Format != PixelFormat.Grayscale8)
            return null;

        var src = page.PixelData;
        var pixelCount = page.Width * page.Height;
        var output = new byte[pixelCount * 4];
        float spread = spreadPx;

        for (int i = 0; i < pixelCount; i++)
        {
            int value = multiChannel
                ? Median(src[i * 4], src[i * 4 + 1], src[i * 4 + 2])
                : src[i];

            // Inverse of the 8-bit encoding 128 +/- |d| * 128 / spread (inside bright), then a
            // one-pixel-wide ramp centred on the edge: coverage at the atlas's own scale.
            float distancePx = (value - 128) * spread / 128f;
            float coverage = Math.Clamp(distancePx + 0.5f, 0f, 1f);
            byte a = (byte)MathF.Round(coverage * 255f, MidpointRounding.AwayFromZero);

            // Premultiplied white: what MonoGame's default AlphaBlend expects.
            output[i * 4] = a;
            output[i * 4 + 1] = a;
            output[i * 4 + 2] = a;
            output[i * 4 + 3] = a;
        }

        return output;
    }

    private static int Median(byte r, byte g, byte b) => Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));
}
