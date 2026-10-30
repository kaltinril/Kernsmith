using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// LCD subpixel rendering for <see cref="AntiAliasMode.Lcd"/>: fills the outline at three times
/// the resolution across the panel's subpixel stripes, runs an <see cref="LcdFilterKernel"/>
/// along that axis, and stores each pixel's three filtered subpixels in its colour channels.
/// </summary>
/// <remarks>
/// Output is <see cref="PixelFormat.Rgba32"/>: R, G and B are the subpixel coverages in panel
/// order, and A is the largest of the three. A = max keeps every pixel a valid premultiplied
/// colour (no channel exceeds alpha), and a fully covered pixel is (255, 255, 255, 255) — the
/// same white-plus-alpha a grayscale glyph is promoted to on an RGBA atlas page.
///
/// Horizontal panels (<see cref="LcdSubpixelOrder.Rgb"/>, <see cref="LcdSubpixelOrder.Bgr"/>)
/// resolve columns in thirds; vertical panels (<see cref="LcdSubpixelOrder.VRgb"/>,
/// <see cref="LcdSubpixelOrder.VBgr"/>) resolve rows in thirds, red (or blue) on top. Both run
/// one code path in a "panel frame" whose first axis is the subpixel axis: for vertical panels
/// that frame is the pixel grid with X and Y exchanged, and the result is written back
/// transposed. A vertical render is therefore exactly the horizontal render of the glyph
/// mirrored across its diagonal, transposed.
///
/// The filter spills ink up to <see cref="LcdFilterKernel.Radius"/> subpixels past the outline,
/// so the bitmap is wider (horizontal panels) or taller (vertical panels) than the coverage box.
/// <see cref="Measure"/> and <see cref="Render"/> share <see cref="Span"/>, which derives the
/// widened span from the outline's tight extent along the subpixel axis, so they cannot
/// disagree. The other axis is the coverage box unchanged.
/// <see cref="RasterOptions.Gamma"/> is not applied, matching the FreeType backend, which applies
/// gamma to grayscale coverage only.
/// </remarks>
internal sealed class LcdRenderer : IGlyphRenderer
{
    private const int OrderCount = 4;

    private static readonly LcdFilterKernel[] Kernels =
        [LcdFilterKernel.PixelBox, LcdFilterKernel.Triangle, LcdFilterKernel.Identity];

    // One shared renderer per kernel and order: they are stateless.
    private static readonly LcdRenderer[] Renderers = CreateAll();

    /// <summary>Maps <see cref="MirrorToPanelFrame"/>'s outline to the panel frame's pixels.</summary>
    private static readonly OutlineTransform PanelFrameToPixels = new(1f, 0f);

    private readonly LcdFilterKernel _filter;
    private readonly LcdSubpixelOrder _order;
    private readonly bool _vertical;
    private readonly bool _blueFirst;

    /// <summary>Creates a renderer with a given filter and panel order.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="order"/> is not a defined layout.</exception>
    public LcdRenderer(LcdFilterKernel filter, LcdSubpixelOrder order)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if ((uint)order >= OrderCount)
            throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown LCD subpixel order.");

        _filter = filter;
        _order = order;
        _vertical = order is LcdSubpixelOrder.VRgb or LcdSubpixelOrder.VBgr;
        _blueFirst = order is LcdSubpixelOrder.Bgr or LcdSubpixelOrder.VBgr;
    }

    /// <summary>The default: pixel-box filter, RGB panel order.</summary>
    public static LcdRenderer Instance => Renderers[0];

    /// <summary>The low-pass kernel run across the subpixels.</summary>
    public LcdFilterKernel Filter => _filter;

    /// <summary>The panel layout the subpixels are stored for.</summary>
    public LcdSubpixelOrder Order => _order;

    /// <summary>The shared renderer for a public filter preset and panel layout.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If either value is not defined.</exception>
    public static LcdRenderer For(LcdFilter filter, LcdSubpixelOrder order)
    {
        var kernel = LcdFilterKernel.For(filter);
        if ((uint)order >= OrderCount)
            throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown LCD subpixel order.");

        return Renderers[(Array.IndexOf(Kernels, kernel) * OrderCount) + (int)order];
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        var (first, count) = Span(glyph);
        return Metrics(glyph, first, count);
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        var (first, count) = Span(glyph);
        var frame = PanelFrame(glyph.Box);
        int lines = frame.Height;

        // Flatten in pixel space with a third of the usual tolerance, then stretch the subpixel
        // axis by three: the flattening error, measured in subpixels, matches what the
        // grayscale path allows in pixels. Vertical panels flatten the outline mirrored into
        // the panel frame (the flattener drops horizontal edges, so the exchange has to happen
        // before it, not on its output); the rasterizer's coverage is the magnitude of the
        // winding, so the mirror's reversed orientation does not matter.
        var edges = _vertical
            ? OutlineFlattener.Flatten(MirrorToPanelFrame(glyph.Outline, glyph.ToPixels), PanelFrameToPixels, OutlineFlattener.DefaultTolerance / 3f)
            : OutlineFlattener.Flatten(glyph.Outline, glyph.ToPixels, OutlineFlattener.DefaultTolerance / 3f);
        for (int i = 0; i < edges.Length; i++)
        {
            var e = edges[i];
            edges[i] = new EdgeSegment(e.X0 * 3f, e.Y0, e.X1 * 3f, e.Y1);
        }

        // One-third-pixel coverage over the same box the grayscale path fills, so ink outside
        // that box is clipped exactly as it is there.
        int subpixelCount = 3 * frame.Width;
        var subpixels = ScanlineRasterizer.Rasterize(
            edges, subpixelCount, lines, 3f * frame.MinX, frame.MinY, AntiAliasMode.Grayscale).Bitmap;

        int width = _vertical ? lines : count;
        int height = _vertical ? count : lines;
        var rgba = new byte[width * height * 4];
        int firstSubpixel = 3 * (first - frame.MinX);

        for (int line = 0; line < lines; line++)
        {
            var row = new ReadOnlySpan<byte>(subpixels, line * subpixelCount, subpixelCount);

            for (int p = 0; p < count; p++)
            {
                int s = firstSubpixel + (3 * p);
                byte near = _filter.Sample(row, s);
                byte middle = _filter.Sample(row, s + 1);
                byte far = _filter.Sample(row, s + 2);

                int output = _vertical
                    ? ((p * width) + line) * 4
                    : ((line * width) + p) * 4;
                rgba[output] = _blueFirst ? far : near;
                rgba[output + 1] = middle;
                rgba[output + 2] = _blueFirst ? near : far;
                rgba[output + 3] = Math.Max(near, Math.Max(middle, far));
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
            Metrics = Metrics(glyph, first, count),
            Format = PixelFormat.Rgba32
        };
    }

    /// <summary>The glyph's metrics for a subpixel-axis span starting at pixel
    /// <paramref name="first"/> and <paramref name="count"/> pixels long.</summary>
    private GlyphMetrics Metrics(PreparedGlyph glyph, int first, int count)
    {
        var box = glyph.Box;
        return _vertical
            ? new GlyphMetrics(BearingX: box.BearingX, BearingY: -first, Advance: glyph.Advance, Width: box.Width, Height: count)
            : new GlyphMetrics(BearingX: first, BearingY: box.BearingY, Advance: glyph.Advance, Width: count, Height: box.Height);
    }

    /// <summary>
    /// The outline mirrored across the pixel grid's diagonal, as an outline that
    /// <see cref="PanelFrameToPixels"/> maps to pixel (py, px) wherever the original mapped to
    /// (px, py). That transform is a unit scale with the usual Y flip, so a point is stored as
    /// (py, -px).
    /// </summary>
    private static GlyphOutline MirrorToPanelFrame(GlyphOutline outline, OutlineTransform toPixels)
    {
        var commands = new OutlineCommand[outline.Commands.Length];
        for (int i = 0; i < commands.Length; i++)
        {
            var c = outline.Commands[i];
            commands[i] = c with
            {
                EndX = toPixels.ToPixelY(c.EndY),
                EndY = -toPixels.ToPixelX(c.EndX),
                Ctrl1X = toPixels.ToPixelY(c.Ctrl1Y),
                Ctrl1Y = -toPixels.ToPixelX(c.Ctrl1X),
                Ctrl2X = toPixels.ToPixelY(c.Ctrl2Y),
                Ctrl2Y = -toPixels.ToPixelX(c.Ctrl2X),
            };
        }

        return GlyphOutline.FromCommands(commands);
    }

    /// <summary>The coverage box in the panel frame: unchanged for horizontal panels, X and Y
    /// exchanged for vertical ones.</summary>
    private GlyphBox PanelFrame(GlyphBox box) =>
        _vertical ? new GlyphBox(box.MinY, box.MinX, box.Height, box.Width) : box;

    /// <summary>
    /// The pixels the filtered glyph occupies along the subpixel axis (columns for horizontal
    /// panels, rows for vertical ones): the first, relative to the pen or the baseline, and how
    /// many.
    /// </summary>
    /// <remarks>
    /// Coverage can only be non-zero on subpixels the outline's tight extent along that axis
    /// touches, clipped to the coverage box; the filter then spreads it
    /// <see cref="LcdFilterKernel.Radius"/> subpixels each way. Rounding that span outward to
    /// whole pixels gives a box that holds every non-zero filtered subpixel without the blank
    /// column a fixed one-pixel pad would usually add.
    /// </remarks>
    private (int First, int Count) Span(PreparedGlyph glyph)
    {
        var frame = PanelFrame(glyph.Box);
        int boxStart = 3 * frame.MinX;
        int boxEnd = 3 * (frame.MinX + frame.Width);

        var tight = OutlineBounds.Tight(glyph.Outline);
        var toPixels = glyph.ToPixels;

        // Pixel Y runs down, so the outline's top (YMax) is the smallest pixel row.
        float low = _vertical ? toPixels.ToPixelY(tight.YMax) : toPixels.ToPixelX(tight.XMin);
        float high = _vertical ? toPixels.ToPixelY(tight.YMin) : toPixels.ToPixelX(tight.XMax);

        int inkStart = Math.Max(boxStart, (int)MathF.Floor(low * 3f));
        int inkEnd = Math.Min(boxEnd, (int)MathF.Ceiling(high * 3f));
        if (inkEnd <= inkStart)
        {
            inkStart = boxStart;
            inkEnd = boxEnd;
        }

        int first = FloorDivide(inkStart - _filter.Radius, 3);
        int last = -FloorDivide(-(inkEnd + _filter.Radius), 3);
        return (first, last - first);
    }

    private static int FloorDivide(int value, int divisor) =>
        (int)Math.Floor(value / (double)divisor);

    private static LcdRenderer[] CreateAll()
    {
        var renderers = new LcdRenderer[Kernels.Length * OrderCount];
        for (int k = 0; k < Kernels.Length; k++)
        {
            for (int o = 0; o < OrderCount; o++)
                renderers[(k * OrderCount) + o] = new LcdRenderer(Kernels[k], (LcdSubpixelOrder)o);
        }

        return renderers;
    }
}
