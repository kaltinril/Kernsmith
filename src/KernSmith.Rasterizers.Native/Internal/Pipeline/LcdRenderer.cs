using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// LCD subpixel rendering for <see cref="AntiAliasMode.Lcd"/>: fills the outline at three times
/// the horizontal resolution, runs an <see cref="LcdFilter"/> across each row of one-third-pixel
/// coverage, and stores each pixel's three filtered subpixels in its colour channels.
/// </summary>
/// <remarks>
/// Output is <see cref="PixelFormat.Rgba32"/>: R, G and B are the subpixel coverages in panel
/// order, and A is the largest of the three. A = max keeps every pixel a valid premultiplied
/// colour (no channel exceeds alpha), and a fully covered pixel is (255, 255, 255, 255) — the
/// same white-plus-alpha a grayscale glyph is promoted to on an RGBA atlas page.
///
/// The filter spills ink up to <see cref="LcdFilter.Radius"/> subpixels past the outline, so
/// the bitmap is wider than the coverage box. <see cref="Measure"/> and <see cref="Render"/>
/// share <see cref="Columns"/>, which derives the widened span from the outline's tight
/// horizontal extent, so they cannot disagree. Vertical extent is the coverage box unchanged.
/// <see cref="RasterOptions.Gamma"/> is not applied, matching the FreeType backend, which applies
/// gamma to grayscale coverage only.
/// </remarks>
internal sealed class LcdRenderer : IGlyphRenderer
{
    /// <summary>The default: pixel-box filter, RGB panel order.</summary>
    public static readonly LcdRenderer Instance = new(LcdFilter.PixelBox, LcdSubpixelOrder.Rgb);

    private readonly LcdFilter _filter;
    private readonly LcdSubpixelOrder _order;

    /// <summary>Creates a renderer with a given filter and panel order.</summary>
    public LcdRenderer(LcdFilter filter, LcdSubpixelOrder order)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filter = filter;
        _order = order;
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        var (left, width) = Columns(glyph);
        return new GlyphMetrics(
            BearingX: left,
            BearingY: glyph.Box.BearingY,
            Advance: glyph.Advance,
            Width: width,
            Height: glyph.Box.Height);
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        var (left, width) = Columns(glyph);
        int height = box.Height;

        // Flatten in pixel space with a third of the usual tolerance, then stretch X by three:
        // the horizontal flattening error, measured in subpixels, matches what the grayscale
        // path allows in pixels.
        var edges = OutlineFlattener.Flatten(glyph.Outline, glyph.ToPixels, OutlineFlattener.DefaultTolerance / 3f);
        for (int i = 0; i < edges.Length; i++)
        {
            var e = edges[i];
            edges[i] = new EdgeSegment(e.X0 * 3f, e.Y0, e.X1 * 3f, e.Y1);
        }

        // One-third-pixel coverage over the same box the grayscale path fills, so ink outside
        // that box is clipped exactly as it is there.
        int subpixelWidth = 3 * box.Width;
        var subpixels = ScanlineRasterizer.Rasterize(
            edges, subpixelWidth, height, 3f * box.MinX, box.MinY, AntiAliasMode.Grayscale).Bitmap;

        var rgba = new byte[width * height * 4];
        int firstSubpixel = 3 * (left - box.MinX);
        bool bgr = _order == LcdSubpixelOrder.Bgr;

        for (int y = 0; y < height; y++)
        {
            var row = new ReadOnlySpan<byte>(subpixels, y * subpixelWidth, subpixelWidth);
            int output = y * width * 4;

            for (int x = 0; x < width; x++, output += 4)
            {
                int s = firstSubpixel + (3 * x);
                byte leftSub = _filter.Sample(row, s);
                byte middle = _filter.Sample(row, s + 1);
                byte rightSub = _filter.Sample(row, s + 2);

                rgba[output] = bgr ? rightSub : leftSub;
                rgba[output + 1] = middle;
                rgba[output + 2] = bgr ? leftSub : rightSub;
                rgba[output + 3] = Math.Max(leftSub, Math.Max(middle, rightSub));
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = rgba,
            Width = width,
            Height = height,
            Pitch = width * 4,
            Metrics = new GlyphMetrics(
                BearingX: left,
                BearingY: box.BearingY,
                Advance: glyph.Advance,
                Width: width,
                Height: height),
            Format = PixelFormat.Rgba32
        };
    }

    /// <summary>
    /// The pixel columns the filtered glyph occupies: its first column (relative to the pen) and
    /// its width.
    /// </summary>
    /// <remarks>
    /// Coverage can only be non-zero on subpixels the outline's tight horizontal extent touches,
    /// clipped to the coverage box; the filter then spreads it <see cref="LcdFilter.Radius"/>
    /// subpixels each way. Rounding that span outward to whole pixels gives a box that holds
    /// every non-zero filtered subpixel without the blank column a fixed one-pixel pad would
    /// usually add.
    /// </remarks>
    private (int Left, int Width) Columns(PreparedGlyph glyph)
    {
        var box = glyph.Box;
        int boxStart = 3 * box.MinX;
        int boxEnd = 3 * (box.MinX + box.Width);

        var tight = OutlineBounds.Tight(glyph.Outline);
        int inkStart = Math.Max(boxStart, (int)MathF.Floor(glyph.ToPixels.ToPixelX(tight.XMin) * 3f));
        int inkEnd = Math.Min(boxEnd, (int)MathF.Ceiling(glyph.ToPixels.ToPixelX(tight.XMax) * 3f));
        if (inkEnd <= inkStart)
        {
            inkStart = boxStart;
            inkEnd = boxEnd;
        }

        int left = FloorDivide(inkStart - _filter.Radius, 3);
        int right = -FloorDivide(-(inkEnd + _filter.Radius), 3);
        return (left, right - left);
    }

    private static int FloorDivide(int value, int divisor) =>
        (int)Math.Floor(value / (double)divisor);
}
